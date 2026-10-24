using System.Text;

namespace PhoneticAnnotator.Core;

internal static class JapaneseReadingConversion
{
    private static readonly Dictionary<string, string> Romaji = CreateRomaji();

    internal static bool TryConvert(string reading, JapaneseScript script, out string result)
    {
        result = reading;
        if (string.IsNullOrEmpty(reading) ||
            reading.EnumerateRunes().Any(rune => rune == Rune.ReplacementChar))
            return false;
        var hiragana = new StringBuilder();
        foreach (var character in reading)
        {
            if (character is '\u3099' or '\u309A')
            {
                if (hiragana.Length == 0 || !TryComposeVoicing(hiragana[^1], character, out var composed))
                    return false;
                hiragana[^1] = composed;
            }
            else if (character is >= '\u3041' and <= '\u3096' or '\u30FC')
                hiragana.Append(character);
            else if (character is >= '\u30A1' and <= '\u30F6')
                hiragana.Append((char)(character - 0x60));
            else
                return false;
        }
        if (script == JapaneseScript.Hiragana)
            result = hiragana.ToString();
        else if (script == JapaneseScript.Katakana)
        {
            for (var index = 0; index < hiragana.Length; index++)
                if (hiragana[index] != 'ー')
                    hiragana[index] = (char)(hiragana[index] + 0x60);
            result = hiragana.ToString();
        }
        else
            return TryRomanize(hiragana.ToString(), out result);
        return true;
    }

    private static bool TryComposeVoicing(char kana, char mark, out char composed)
    {
        const string voicedBases = "かきくけこさしすせそたちつてとはひふへほう";
        const string voicedForms = "がぎぐげござじずぜぞだぢづでどばびぶべぼゔ";
        const string semiVoicedBases = "はひふへほ";
        const string semiVoicedForms = "ぱぴぷぺぽ";
        var bases = mark == '\u3099' ? voicedBases : semiVoicedBases;
        var forms = mark == '\u3099' ? voicedForms : semiVoicedForms;
        var index = bases.IndexOf(kana);
        composed = index < 0 ? kana : forms[index];
        return index >= 0;
    }

    private static bool TryRomanize(string reading, out string result)
    {
        result = reading;
        var output = new StringBuilder();
        var geminate = false;
        for (var index = 0; index < reading.Length; index++)
        {
            var character = reading[index];
            if (character == 'っ')
            {
                if (geminate)
                    return false;
                geminate = true;
                continue;
            }
            if (character == 'ー')
            {
                if (geminate || output.Length == 0 || !"aeiou".Contains(output[^1]))
                    return false;
                output.Append(output[^1]);
                continue;
            }
            string? syllable;
            if (index + 1 < reading.Length && Romaji.TryGetValue(reading.Substring(index, 2), out syllable))
                index++;
            else if (!Romaji.TryGetValue(character.ToString(), out syllable))
                return false;

            if (geminate)
            {
                if ("aeiouny".Contains(syllable[0]))
                    return false;
                output.Append(syllable.StartsWith("ch", StringComparison.Ordinal) ? 't' : syllable[0]);
                geminate = false;
            }
            output.Append(syllable);
            if (character == 'ん' && index + 1 < reading.Length &&
                "あいうえおやゆよぁぃぅぇぉゃゅょ".Contains(reading[index + 1]))
                output.Append('\'');
        }
        if (geminate)
            return false;
        result = output.ToString();
        return true;
    }

    private static Dictionary<string, string> CreateRomaji()
    {
        var dictionary = new Dictionary<string, string>(StringComparer.Ordinal);
        // Keep the rows explicit so exceptional Hepburn spellings do not depend on arithmetic.
        var rows = new[]
        {
            ("あいうえお", "a i u e o"), ("かきくけこ", "ka ki ku ke ko"),
            ("がぎぐげご", "ga gi gu ge go"), ("さしすせそ", "sa shi su se so"),
            ("ざじずぜぞ", "za ji zu ze zo"), ("たちつてと", "ta chi tsu te to"),
            ("だぢづでど", "da ji zu de do"), ("なにぬねの", "na ni nu ne no"),
            ("はひふへほ", "ha hi fu he ho"), ("ばびぶべぼ", "ba bi bu be bo"),
            ("ぱぴぷぺぽ", "pa pi pu pe po"), ("まみむめも", "ma mi mu me mo"),
            ("やゆよ", "ya yu yo"), ("らりるれろ", "ra ri ru re ro"),
            ("わゐゑをんゔ", "wa wi we o n vu")
        };
        foreach (var (characters, spellings) in rows)
        {
            var values = spellings.Split(' ');
            for (var index = 0; index < characters.Length; index++)
                dictionary.Add(characters[index].ToString(), values[index]);
        }
        foreach (var (kanaBase, consonant) in new[]
        {
            ("き", "ky"), ("ぎ", "gy"), ("し", "sh"), ("じ", "j"),
            ("ち", "ch"), ("ぢ", "j"), ("に", "ny"), ("ひ", "hy"),
            ("び", "by"), ("ぴ", "py"), ("み", "my"), ("り", "ry")
        })
        {
            dictionary.Add(kanaBase + "ゃ", consonant + "a");
            dictionary.Add(kanaBase + "ゅ", consonant + "u");
            dictionary.Add(kanaBase + "ょ", consonant + "o");
        }
        foreach (var (characters, spelling) in new[]
        {
            ("いぇ", "ye"), ("うぃ", "wi"), ("うぇ", "we"), ("うぉ", "wo"),
            ("しぇ", "she"), ("じぇ", "je"), ("ちぇ", "che"),
            ("てぃ", "ti"), ("でぃ", "di"), ("てゅ", "tyu"), ("でゅ", "dyu"),
            ("とぅ", "tu"), ("どぅ", "du"), ("つぁ", "tsa"), ("つぃ", "tsi"),
            ("つぇ", "tse"), ("つぉ", "tso"), ("ふぁ", "fa"), ("ふぃ", "fi"),
            ("ふぇ", "fe"), ("ふぉ", "fo"), ("ふゅ", "fyu"),
            ("ゔぁ", "va"), ("ゔぃ", "vi"), ("ゔぇ", "ve"), ("ゔぉ", "vo"), ("ゔゅ", "vyu")
        })
            dictionary.Add(characters, spelling);
        return dictionary;
    }
}
