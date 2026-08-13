using System.Text;
using System.Text.RegularExpressions;
using Neni.Abstractions.Enums;

namespace Neni.Ocr.Services;

// Sistema de escritura del texto reconocido (agrupa idiomas, no es 1:1 con ellos:
// ES y EN comparten perfil Latin; JA y ZH comparten perfil Cjk).
public enum Script
{
    Latin,
    Cjk
}

// Normalización de texto OCR con perfiles por SISTEMA DE ESCRITURA (no por idioma).
// Repara artefactos propios del reconocimiento óptico; nunca filtra contenido
// "desconocido" — lo único que se descarta son caracteres de control/ancho-cero que no
// son texto real. Ese criterio importa porque un filtro tipo [^\x00-\x7F] borraría
// acentos/ñ del español y el 100% del texto japonés/chino.
// Sin estado: seguro de llamar desde cualquier hilo del ciclo de captura.
public static partial class TextNormalizer
{
    // Deriva el perfil desde el idioma configurado en Settings. Cualquier idioma no-CJK
    // cae al perfil Latin por defecto: es el fallback seguro, nunca destruye contenido.
    public static Script ScriptFor(Languages language) => language switch
    {
        Languages.Japanese or Languages.Chinese => Script.Cjk,
        _ => Script.Latin
    };

    private sealed record Profile(
        string JoinLinesWith,
        bool FixLinebreakHyphens,
        bool StripIntraCjkSpaces,
        bool FoldFullwidthAlnum,
        bool FixMissingSpaceAfterPunct,
        bool FoldCjkPunctToAscii);

    private static readonly Profile LatinProfile = new(
        JoinLinesWith: " ",
        FixLinebreakHyphens: true,
        StripIntraCjkSpaces: false,
        FoldFullwidthAlnum: false,
        FixMissingSpaceAfterPunct: true,
        FoldCjkPunctToAscii: true);

    private static readonly Profile CjkProfile = new(
        JoinLinesWith: "",
        FixLinebreakHyphens: false,
        StripIntraCjkSpaces: true,
        FoldFullwidthAlnum: true,
        FixMissingSpaceAfterPunct: false,
        FoldCjkPunctToAscii: false);

    public static string Normalize(string text, Script script)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        Profile profile = script == Script.Cjk ? CjkProfile : LatinProfile;

        // alch esto si lo hizo claude jajs
        // Componer acentos descompuestos (algunos motores emiten "e" + "´" en vez de
        //    "é"); sin esto, dos lecturas del mismo texto fallan la caché de traducción.
        text = text.Normalize(NormalizationForm.FormC);

        // Unificar saltos de línea antes de todo lo que razona sobre '\n'.
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');

        // Basura garantizada (controles, zero-width, BOM, U+FFFD) — nunca contenido real.
        text = ControlOrZeroWidthRegex().Replace(text, "");

        // Puntuación "equivalente" que el OCR alterna al azar entre variantes:
        //    unificarla convierte fallos de caché en aciertos y evita re-traducciones.
        text = EllipsisRegex().Replace(text, "…");
        text = FoldChars(text, FoldCurlyQuote);

        // Las cajas de diálogo cortan palabras al final de línea con guion; solo tiene
        // sentido repararlo en escritura latina (el corte silábico no aplica a CJK).
        if (profile.FixLinebreakHyphens)
            text = LinebreakHyphenRegex().Replace(text, "$1$2");

        text = text.Replace("\n", profile.JoinLinesWith);

        // Puntuación ideográfica que se cuela en texto latino (ej. "I swear。"): en el
        // perfil CJK esos caracteres son contenido legítimo y no se tocan.
        if (profile.FoldCjkPunctToAscii)
            text = FoldChars(text, FoldCjkPunctuation);

        // El OCR pega palabras tras la puntuación ("Yeah.Ican't"). Exigir letra a ambos
        // lados protege decimales ("3.5", "1,000") y elipsis ya plegadas a "…".
        if (profile.FixMissingSpaceAfterPunct)
            text = MissingSpaceAfterPunctRegex().Replace(text, "$1 ");

        // Ancho completo -> ASCII, SOLO letras y dígitos (no puntuación: "！？「」" son
        // contenido legítimo en japonés/chino). Tabla explícita en vez de NFKC: NFKC
        // también descompondría contenido CJK legítimo (ligaduras, símbolos como ㊙).
        if (profile.FoldFullwidthAlnum)
            text = FoldChars(text, FoldFullwidthAlnum);

        // Espacios espurios DENTRO de una frase CJK; se exige CJK a ambos lados para no
        // tocar espacios que separan un nombre propio o número latino embebido.
        if (profile.StripIntraCjkSpaces)
            text = CjkSpaceBetweenRegex().Replace(text, "");

        text = WhitespaceRunRegex().Replace(text, " ");

        return text.Trim();
    }

    // Aplica un mapeo 1:1 de caracteres sin asignar si nada cambia.
    private static string FoldChars(string text, Func<char, char> fold)
    {
        StringBuilder? builder = null;
        for (int i = 0; i < text.Length; i++)
        {
            char folded = fold(text[i]);
            if (builder == null)
            {
                if (folded == text[i])
                    continue;
                builder = new StringBuilder(text.Length).Append(text, 0, i);
            }
            builder.Append(folded);
        }
        return builder?.ToString() ?? text;
    }

    private static char FoldCurlyQuote(char c) => c switch
    {
        '‘' or '’' => '\'',  // ‘ ’
        '“' or '”' => '"',   // “ ”
        _ => c
    };

    private static char FoldCjkPunctuation(char c) => c switch
    {
        '。' => '.',  // 。
        '、' => ',',  // 、
        '，' => ',',  // ，
        '！' => '!',  // ！
        '？' => '?',  // ？
        '：' => ':',  // ：
        '；' => ';',  // ；
        _ => c
    };

    private static char FoldFullwidthAlnum(char c) => c switch
    {
        // ０-９, Ａ-Ｚ, ａ-ｚ -> sus equivalentes ASCII (offset fijo 0xFEE0)
        >= '０' and <= '９' => (char)(c - 0xFEE0),
        >= 'Ａ' and <= 'Ｚ' => (char)(c - 0xFEE0),
        >= 'ａ' and <= 'ｚ' => (char)(c - 0xFEE0),
        _ => c
    };

    // Controles C0/C1 (salvo '\t' y '\n', que se manejan como espacio en blanco) + BOM +
    // zero-width + marcas direccionales + carácter de reemplazo Unicode.
    [GeneratedRegex(@"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F-\x9F​-‏‪-‮﻿�]")]
    private static partial Regex ControlOrZeroWidthRegex();

    [GeneratedRegex(@"\.{3,}")]
    private static partial Regex EllipsisRegex();

    [GeneratedRegex(@"(\w)-\n(\w)")]
    private static partial Regex LinebreakHyphenRegex();

    // Letra + [.,!?] + letra, sin espacio: se corre DESPUÉS de plegar "..." a "…" para
    // que las elipsis nunca coincidan.
    [GeneratedRegex(@"(?<=\p{L})([.,!?])(?=\p{L})")]
    private static partial Regex MissingSpaceAfterPunctRegex();

    // Rangos CJK: puntuación+kana (U+3000-U+30FF, incluye 「」『』。、・〜), ideogramas
    // unificados (U+3400-U+9FFF) y de compatibilidad (U+F900-U+FAFF).
    [GeneratedRegex(@"(?<=[　-ヿ㐀-鿿豈-﫿])[ 　]+(?=[　-ヿ㐀-鿿豈-﫿])")]
    private static partial Regex CjkSpaceBetweenRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRunRegex();
}
