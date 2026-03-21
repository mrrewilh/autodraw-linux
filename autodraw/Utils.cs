using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Numerics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SkiaSharp;
using Vector = Avalonia.Vector;

namespace Autodraw;

/// <summary>
/// Replaces SharpHook.Native.KeyCode for keybind configuration.
/// Values chosen to match SharpHook's integer ordinals so existing
/// config.json files remain compatible.
/// </summary>
public enum KeyCode
{
    VcUndefined = 0,
    VcEscape = 1,
    Vc1 = 2, Vc2 = 3, Vc3 = 4, Vc4 = 5,
    Vc5 = 6, Vc6 = 7, Vc7 = 8, Vc8 = 9, Vc9 = 10,
    Vc0 = 11,
    VcMinus = 12, VcEquals = 13,
    VcBackspace = 14, VcTab = 15,
    VcQ = 16, VcW = 17, VcE = 18,
    VcR = 19, VcT = 20, VcY = 21,
    VcU = 23, VcI = 24, VcO = 25, VcP = 26,
    VcOpenBracket = 27, VcCloseBracket = 28,
    VcEnter = 96,
    VcLeftControl = 29,
    VcA = 30, VcS = 31, VcD = 32,
    VcF = 33, VcG = 34, VcH = 35,
    VcJ = 36, VcK = 37, VcL = 38,
    VcSemiColon = 39, VcQuote = 40,
    VcBackQuote = 41,
    VcLeftShift = 42,
    VcBackslash = 43,
    VcZ = 44, VcX = 45, VcC = 46,
    VcV = 47, VcB = 48, VcN = 49, VcM = 50,
    VcComma = 51, VcPeriod = 52, VcSlash = 53,
    VcRightShift = 54,
    VcLeftAlt = 56,
    VcSpace = 57,
    VcF1 = 59, VcF2 = 60, VcF3 = 61, VcF4 = 62,
    VcF5 = 63, VcF6 = 64, VcF7 = 65, VcF8 = 66,
    VcF9 = 67, VcF10 = 68, VcF11 = 87, VcF12 = 88,
    VcRightControl = 97,
    VcRightAlt = 100,
    VcDelete = 111,
}

public static class ImageExtensions
{
    public static Bitmap ConvertToAvaloniaBitmap(this SKBitmap bitmap)
    {
        using var encodedStream = new MemoryStream();
        bitmap.Encode(encodedStream, SKEncodedImageFormat.Png, 100);
        encodedStream.Seek(0, SeekOrigin.Begin);
        return new Bitmap(encodedStream);
    }
}

public class Config
{
    public static string FolderPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoDraw");

    public static KeyCode Keybind_StartDrawing = KeyCode.VcLeftShift;
    public static KeyCode Keybind_StopDrawing = KeyCode.VcLeftAlt;
    public static KeyCode Keybind_PauseDrawing = KeyCode.VcBackslash;
    public static KeyCode Keybind_SkipRescan = KeyCode.VcBackspace;
    public static KeyCode Keybind_LockPreview = KeyCode.VcLeftControl;
    public static KeyCode Keybind_ClearLock = KeyCode.VcBackQuote;
    
    public static Vector2 Preview_LastLockPos = new(0,0);

    public static string ConfigPath = Path.Combine(FolderPath, "config.json");
    public static string ThemesPath = Path.Combine(FolderPath, "Themes");
    public static string CachePath = Path.Combine(FolderPath, "Cache");

    public static void init()
    {
        Directory.CreateDirectory(FolderPath);
        if (!File.Exists(ConfigPath))
        {
            JObject obj = new();
            // Migrates old directory list path (from AutoDraw v1) to the new config file
            var OldPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AutoDraw");
            if (File.Exists(Path.Combine(OldPath, "dir.txt")) &&
                File.ReadAllText(Path.Combine(OldPath, "dir.txt")).Length != 0)
                obj.Add("ConfigFolder", File.ReadAllText(Path.Combine(OldPath, "dir.txt")));
            var emptyJObject = JsonConvert.SerializeObject(obj);
            File.WriteAllText(ConfigPath, emptyJObject);
            
        }

        Utils.Copy(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Styles"), ThemesPath);
        
        // Check Configuration Path for Themes
        if (GetEntry("SavedThemesPath") is null || !Directory.Exists(GetEntry("SavedPath")))
        {
            Directory.CreateDirectory(ThemesPath);
            SetEntry("SavedThemesPath", ThemesPath);
        }
        else
        {
            ThemesPath = GetEntry("SavedThemesPath")!;
        }
        
        // Check Configuration Path for Cache
        if (GetEntry("SavedCachePath") is null || !Directory.Exists(GetEntry("SavedPath")))
        {
            Directory.CreateDirectory(CachePath);
            SetEntry("SavedCachePath", CachePath);
        }
        else
        {
            CachePath = GetEntry("SavedCachePath")!;
        }
        
        // Get Keybinds
        if (GetEntry("Keybind_StartDrawing") is not null)
        {
            Keybind_StartDrawing = (KeyCode)Enum.Parse(typeof(KeyCode), GetEntry("Keybind_StartDrawing")!);
        }
        if (GetEntry("Keybind_StopDrawing") is not null)
        {
            Keybind_StopDrawing = (KeyCode)Enum.Parse(typeof(KeyCode), GetEntry("Keybind_StopDrawing")!);
        }
        if (GetEntry("Keybind_PauseDrawing") is not null)
        {
            Keybind_PauseDrawing = (KeyCode)Enum.Parse(typeof(KeyCode), GetEntry("Keybind_PauseDrawing")!);
        }
        if (GetEntry("Keybind_SkipRescan") is not null)
        {
            Keybind_SkipRescan = (KeyCode)Enum.Parse(typeof(KeyCode), GetEntry("Keybind_SkipRescan")!);
        }
        if (GetEntry("Keybind_LockPreview") is not null)
        {
            Keybind_LockPreview = (KeyCode)Enum.Parse(typeof(KeyCode), GetEntry("Keybind_LockPreview")!);
        }
        if (GetEntry("Keybind_ClearLock") is not null)
        {
            Keybind_ClearLock = (KeyCode)Enum.Parse(typeof(KeyCode), GetEntry("Keybind_ClearLock")!);
        }
        
        if (GetEntry("Preview_LastLockedX") is not null && GetEntry("Preview_LastLockedY") is not null )
        {
            Preview_LastLockPos = new Vector2(int.Parse(GetEntry("Preview_LastLockedX")!),int.Parse(GetEntry("Preview_LastLockedY")!));
        }
        
    }

    public static string? GetEntry(string entry)
    {
        if (!File.Exists(ConfigPath)) return null;
        var json = File.ReadAllText(ConfigPath);
        var parse = JObject.Parse(json);
        return (string?)parse[entry];
    }

    public static bool SetEntry(string entry, string data)
    {
        if (!File.Exists(ConfigPath)) return false;
        var json = File.ReadAllText(ConfigPath);
        var jsonFile = JObject.Parse(json);
        jsonFile[entry] = data;
        File.WriteAllText(ConfigPath, JsonConvert.SerializeObject(jsonFile, Formatting.Indented));
        return true;
    }
}

public class Utils
{
    public static string LogFolder = Path.Combine(Config.FolderPath, "logs");
    public static string LogsPath = Path.Combine(LogFolder, $"{DateTime.Now:dd.MM.yyyy}.txt");
    public static bool LoggingEnabled = Config.GetEntry("logsEnabled") == "True";
    public static StreamWriter? LogObject;

    public static void Log(object data)
    {
        string text = data.ToString() ?? "null";
        Console.WriteLine(text);
        if (!LoggingEnabled) return;
        if (!Directory.Exists(LogFolder)) Directory.CreateDirectory(LogFolder);
        if (LogObject == null) LogObject = new StreamWriter(LogsPath);
        LogObject.WriteLine(text);
        LogObject.Flush();
    }

    public static void Copy(string sourceDirectory, string targetDirectory)
    {
        var diSource = new DirectoryInfo(sourceDirectory);
        var diTarget = new DirectoryInfo(targetDirectory);
        CopyAll(diSource, diTarget);
    }

    public static void CopyAll(DirectoryInfo source, DirectoryInfo target)
    {
        Directory.CreateDirectory(target.FullName);

        // Copy each file into the new directory.
        foreach (var fi in source.GetFiles())
        {
            Console.WriteLine(@"Copying {0}\{1}", target.FullName, fi.Name);
            fi.CopyTo(Path.Combine(target.FullName, fi.Name), true);
        }

        // Copy each subdirectory using recursion.
        foreach (var diSourceSubDir in source.GetDirectories())
        {
            var nextTargetSubDir =
                target.CreateSubdirectory(diSourceSubDir.Name);
            CopyAll(diSourceSubDir, nextTargetSubDir);
        }
    }
}


public class Marketplace
{
    public static string API = "https://auto-draw.com/api/";
    private static readonly HttpClient client = new HttpClient();
    
    public async static Task<JArray> List(string type) // 'type' can be either "theme" or "config"
    {
        HttpResponseMessage response = await client.GetAsync($"{API}list?page=1&filter={type}");
        if (!response.IsSuccessStatusCode) return null!;
        var JSONResponse = await response.Content.ReadAsStringAsync();
        dynamic Response = (JObject)JsonConvert.DeserializeObject(JSONResponse);
        return Response.items;
    }
    
    public async static Task<String> Download(int id) // filename should be in the format of "{theme name}.{file extension}"
    {
        HttpResponseMessage response = await client.GetAsync($"{API}download?id={id}");
        if (!response.IsSuccessStatusCode) return null!;
        var FileResponse = await response.Content.ReadAsStringAsync();
        File.WriteAllText(Path.Combine(Config.ThemesPath, response.Content.Headers.ContentDisposition?.FileName.Trim('"')), FileResponse);
        return Path.Combine(Config.ThemesPath, response.Content.Headers.ContentDisposition?.FileName.Trim('"'));
    }
}