using System.Runtime.InteropServices;
using Rozkladka.Core;

namespace Rozkladka;

/// <summary>
/// Словники Windows (Spell Checking API, Windows 8+). Працює для мов, встановлених у системі;
/// додаткові словники Hunspell можна покласти в %AppData%\Microsoft\Spelling\&lt;мова&gt;.
/// </summary>
internal sealed class WindowsSpellChecker : IWordChecker
{
    private readonly Dictionary<string, ISpellChecker?> _checkers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string, string), bool> _cache = new();
    private ISpellCheckerFactory? _factory;
    private bool _factoryFailed;

    public bool? IsWord(string word, string languageTag)
    {
        var key = (languageTag, word.ToLowerInvariant());
        if (_cache.TryGetValue(key, out var cached)) return cached;

        var checker = GetChecker(languageTag);
        if (checker is null) return null;
        try
        {
            if (checker.Check(word, out var errors) != 0 || errors is null) return null;
            bool ok;
            try
            {
                ok = errors.Next(out var error) != 0; // S_FALSE (1) — помилок немає
                if (error != IntPtr.Zero) Marshal.Release(error);
            }
            finally
            {
                Marshal.ReleaseComObject(errors);
            }
            if (_cache.Count > 20000) _cache.Clear();
            _cache[key] = ok;
            return ok;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public bool IsSupported(string languageTag) => GetChecker(languageTag) is not null;

    private ISpellChecker? GetChecker(string tag)
    {
        if (_checkers.TryGetValue(tag, out var existing)) return existing;
        ISpellChecker? checker = null;
        try
        {
            var factory = GetFactory();
            if (factory is not null)
            {
                foreach (var candidate in new[] { tag, tag.Split('-')[0] })
                {
                    if (factory.IsSupported(candidate, out var supported) == 0 && supported
                        && factory.CreateSpellChecker(candidate, out var created) == 0)
                    {
                        checker = created;
                        break;
                    }
                }
            }
        }
        catch (Exception)
        {
            checker = null;
        }
        _checkers[tag] = checker;
        return checker;
    }

    private ISpellCheckerFactory? GetFactory()
    {
        if (_factory is not null || _factoryFailed) return _factory;
        try
        {
            var type = Type.GetTypeFromCLSID(new Guid("7AB36653-1796-484B-BDFA-E74F1DB7C1DC"));
            _factory = type is null ? null : (ISpellCheckerFactory?)Activator.CreateInstance(type);
        }
        catch (Exception)
        {
            _factory = null;
        }
        _factoryFailed = _factory is null;
        return _factory;
    }

    [ComImport, Guid("8E018A9D-2415-4677-BF08-794EA61F94BB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISpellCheckerFactory
    {
        [PreserveSig] int get_SupportedLanguages(out IntPtr value);
        [PreserveSig] int IsSupported([MarshalAs(UnmanagedType.LPWStr)] string languageTag, [MarshalAs(UnmanagedType.Bool)] out bool value);
        [PreserveSig] int CreateSpellChecker([MarshalAs(UnmanagedType.LPWStr)] string languageTag, out ISpellChecker value);
    }

    [ComImport, Guid("B6FD0B71-E2BC-4653-8D05-F197E412770B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISpellChecker
    {
        [PreserveSig] int get_LanguageTag(out IntPtr value);
        [PreserveSig] int Check([MarshalAs(UnmanagedType.LPWStr)] string text, out IEnumSpellingError value);
    }

    [ComImport, Guid("803E3BD4-2828-4410-8290-418D1D73C762"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEnumSpellingError
    {
        [PreserveSig] int Next(out IntPtr value);
    }
}
