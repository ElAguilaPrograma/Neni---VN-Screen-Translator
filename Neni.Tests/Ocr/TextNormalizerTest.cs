using Neni.Abstractions.Enums;
using Neni.Ocr.Services;

namespace Neni.Tests.Ocr;

// Casos latinos extraídos de salidas REALES del OCR sobre los frames de prueba
// (ver OcrTest con --logger detailed); los casos CJK son sintéticos porque
// Frames/Japanese sigue vacío.
public class TextNormalizerTest
{
    [Theory]
    [InlineData(Languages.English, nameof(Script.Latin))]
    [InlineData(Languages.Spanish, nameof(Script.Latin))]
    [InlineData(Languages.Japanese, nameof(Script.Cjk))]
    [InlineData(Languages.Chinese, nameof(Script.Cjk))]
    [InlineData((Languages)99, nameof(Script.Latin))] // idioma desconocido -> fallback seguro
    public void ScriptFor_mapea_idioma_a_sistema_de_escritura(Languages language, string expected)
    {
        Assert.Equal(expected, TextNormalizer.ScriptFor(language).ToString());
    }

    [Theory]
    // Comillas curvas -> ASCII (frame 27.png: «was there anything you forgot?”»)
    [InlineData("was there anything you forgot?”", "was there anything you forgot?\"")]
    [InlineData("‘sure’ and “fine”", "'sure' and \"fine\"")]
    // Elipsis de 3+ puntos -> "…" (frame 38.png)
    [InlineData("\"That's right...", "\"That's right…")]
    [InlineData("Wait.....what?", "Wait…what?")]
    // Espacio faltante tras puntuación entre letras (frames 39.png, 26.png)
    [InlineData("Yeah.Ican't wait.", "Yeah. Ican't wait.")]
    [InlineData("After that,I rushed to explain", "After that, I rushed to explain")]
    // Puntuación ideográfica colada en texto latino (frame 42.png)
    [InlineData("That Mom,I swear。", "That Mom, I swear.")]
    [InlineData("Really！Are you sure？", "Really! Are you sure?")]
    // Decimales y números intactos (la regla exige letra a AMBOS lados del signo)
    [InlineData("version 3.5 costs 1,000", "version 3.5 costs 1,000")]
    [InlineData("8620 will return. And Kiha", "8620 will return. And Kiha")]
    // ".." no es elipsis ni letra+punto+letra: se conserva (frame 11.png)
    [InlineData("Where are you, Michiko..?", "Where are you, Michiko..?")]
    // Guion de corte de línea reparado
    [InlineData("some-\nthing else", "something else")]
    // Líneas unidas con espacio y espacios colapsados
    [InlineData("line one\nline two", "line one line two")]
    [InlineData("  a \t  b  ", "a b")]
    // Basura de control / zero-width / BOM eliminada
    [InlineData("a​b﻿cd�", "abcd")]
    // Acentos descompuestos compuestos via NFC (e + U+0301 -> é)
    [InlineData("café", "café")]
    public void Normalize_repara_artefactos_en_perfil_latino(string input, string expected)
    {
        Assert.Equal(expected, TextNormalizer.Normalize(input, Script.Latin));
    }

    [Theory]
    // Líneas unidas SIN espacio
    [InlineData("こんにちは\n世界", "こんにちは世界")]
    // Espacios espurios dentro de la frase CJK eliminados
    [InlineData("こん にちは、　世界", "こんにちは、世界")]
    // Espacios alrededor de un número latino embebido se conservan
    [InlineData("結果は 8620 だった", "結果は 8620 だった")]
    // Ancho completo -> ASCII solo letras y dígitos
    [InlineData("ＡＢＣ１２３", "ABC123")]
    // Puntuación de ancho completo es contenido legítimo: intacta
    [InlineData("すごい！？「はい」", "すごい！？「はい」")]
    // Elipsis y comillas curvas también aplican al perfil CJK
    [InlineData("そうか...“はい”", "そうか…\"はい\"")]
    public void Normalize_repara_artefactos_en_perfil_cjk(string input, string expected)
    {
        Assert.Equal(expected, TextNormalizer.Normalize(input, Script.Cjk));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\n")]
    public void Normalize_entrada_vacia_o_solo_espacios_devuelve_vacio(string input)
    {
        Assert.Equal("", TextNormalizer.Normalize(input, Script.Latin));
        Assert.Equal("", TextNormalizer.Normalize(input, Script.Cjk));
    }
}
