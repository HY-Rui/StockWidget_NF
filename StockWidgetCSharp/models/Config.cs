using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace StockWidget;

/// <summary>
/// 配置存取：完全复用 Python 版的 %APPDATA%\StockWidget\SW_config.json。
/// 内部用 JsonNode（动态）以兼容键的多种历史类型（list/dict/bool/数值）。
/// 对应 Python App.load_config / save_config + WidgetPanel.current_config。
/// </summary>
internal static class Config
{
    public static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StockWidget");
    public static readonly string FilePath = Path.Combine(Dir, "SW_config.json");

    /// <summary>读取配置，失败返回空对象（首次启动等价全默认）。</summary>
    public static JsonObject Load()
    {
        try
        {
            if (System.IO.File.Exists(FilePath))
            {
                var text = System.IO.File.ReadAllText(FilePath);
                return JsonNode.Parse(text)?.AsObject() ?? new JsonObject();
            }
        }
        catch { }
        return new JsonObject();
    }

    /// <summary>原子写入：先写 .tmp 再 File.Move 替换（对应 Python save_config）。</summary>
    public static void Save(JsonObject cfg)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var tmp = FilePath + ".tmp";
            var opts = new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
            System.IO.File.WriteAllText(tmp, cfg.ToJsonString(opts));
            System.IO.File.Move(tmp, FilePath, overwrite: true);
        }
        catch { }
    }

    // ===== 类型安全的取值助手（带默认值）=====

    public static string GetStr(JsonObject o, string key, string def)
    {
        if (o.TryGetPropertyValue(key, out var v) && v != null)
        {
            var s = v.ToString();
            return string.IsNullOrEmpty(s) ? def : s;
        }
        return def;
    }

    public static bool GetBool(JsonObject o, string key, bool def)
    {
        if (o.TryGetPropertyValue(key, out var v) && v != null)
        {
            if (v is JsonValue jv && jv.TryGetValue<bool>(out var b)) return b;
            // 兼容字符串 "true"/"false"
            var s = v.ToString().Trim().ToLowerInvariant();
            if (s == "true") return true;
            if (s == "false") return false;
        }
        return def;
    }

    public static int GetInt(JsonObject o, string key, int def)
    {
        if (o.TryGetPropertyValue(key, out var v) && v != null && v is JsonValue jv)
        {
            if (jv.TryGetValue<int>(out var i)) return i;
            if (jv.TryGetValue<long>(out var l)) return (int)l;
            if (jv.TryGetValue<double>(out var d)) return (int)d;
            if (int.TryParse(v.ToString(), out var p)) return p;
        }
        return def;
    }

    public static double GetDouble(JsonObject o, string key, double def)
    {
        if (o.TryGetPropertyValue(key, out var v) && v != null && v is JsonValue jv)
        {
            if (jv.TryGetValue<double>(out var d)) return d;
            if (jv.TryGetValue<long>(out var l)) return l;
            if (jv.TryGetValue<int>(out var i)) return i;
            if (double.TryParse(v.ToString(), out var p)) return p;
        }
        return def;
    }

    /// <summary>取字符串列表（兼容旧 list/dict）。对应 Python codes_cfg 等。</summary>
    public static List<string> GetStrList(JsonObject o, string key)
    {
        if (!o.TryGetPropertyValue(key, out var v) || v is null) return new();
        if (v is JsonArray arr)
        {
            var result = new List<string>(arr.Count);
            foreach (var item in arr)
            {
                if (item != null)
                {
                    var s = item.ToString().Trim();
                    if (!string.IsNullOrEmpty(s)) result.Add(s);
                }
            }
            return result;
        }
        return new();
    }

    /// <summary>取 int->int 字典（用于 col_widths）。</summary>
    public static Dictionary<string, int> GetColWidths(JsonObject o, string key)
    {
        var result = new Dictionary<string, int>();
        if (o.TryGetPropertyValue(key, out var v) && v is JsonObject obj)
        {
            foreach (var kv in obj)
            {
                if (kv.Value != null && kv.Value is JsonValue jv && jv.TryGetValue<int>(out var w))
                    result[kv.Key] = w;
            }
        }
        return result;
    }
}

/// <summary>
/// 背景色结构（对应 Python bg: {r,g,b,a}）。
/// </summary>
internal sealed class BgColor
{
    public int R { get; set; } = 0;
    public int G { get; set; } = 0;
    public int B { get; set; } = 0;
    public int A { get; set; } = 191;  // 0-255
}
