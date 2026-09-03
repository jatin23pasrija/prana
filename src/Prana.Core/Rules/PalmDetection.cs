using System.Globalization;
using System.Text.RegularExpressions;
using Prana.Core.Model;

namespace Prana.Core.Rules;

/// <summary>What can be said about palm in one product.</summary>
public enum PalmState
{
    /// <summary>An ingredient known to be palm derived is named on the label.</summary>
    Present,

    /// <summary>The label states how much. Only ever set from a printed percentage.</summary>
    ConfirmedQuantity,

    /// <summary>
    /// An ingredient that may or may not be palm is named, most often an unnamed vegetable oil.
    /// The honest answer is that the label does not say.
    /// </summary>
    Unknown,

    /// <summary>
    /// The ingredients were read and nothing palm derived was found. This is a statement about
    /// the ingredient list, not about the product, and the wording must keep that distinction.
    /// </summary>
    NotDetected,

    /// <summary>There is no ingredient list to look at.</summary>
    NoIngredients,
}

/// <summary>What the dictionary matched, and where.</summary>
/// <param name="IngredientId">The canonical ingredient.</param>
/// <param name="Name">Its display name.</param>
/// <param name="MatchedText">The wording actually printed on the label.</param>
/// <param name="Percentage">A percentage printed beside it, when the label states one.</param>
public sealed record PalmMatch(string IngredientId, string Name, string MatchedText, double? Percentage);

/// <summary>The full answer for one product.</summary>
public sealed record PalmFinding(
    PalmState State,
    IReadOnlyList<PalmMatch> Definite,
    IReadOnlyList<PalmMatch> Possible)
{
    /// <summary>A sentence written for someone holding the packet.</summary>
    public string Statement => State switch
    {
        PalmState.ConfirmedQuantity when Definite.Count > 0 && Definite[0].Percentage is { } pct =>
            $"Contains {Definite[0].Name.ToLowerInvariant()}, declared as "
            + $"{pct.ToString("0.##", CultureInfo.InvariantCulture)}% on the label.",

        PalmState.Present =>
            $"Contains {Definite[0].Name.ToLowerInvariant()}. The label does not say how much.",

        // Deliberately says nothing about which oil. Most matches here are unnamed vegetable
        // oils, but vitamin A palmitate lands here too, and telling someone their fortified
        // milk powder might contain palm oil because of a trace vitamin would be worse than
        // saying nothing. The ingredient's own explanation carries the specifics.
        PalmState.Unknown =>
            $"The label lists {Possible[0].MatchedText}. That wording does not say whether "
            + "palm was used.",

        PalmState.NotDetected =>
            "No palm-derived ingredient is named in the ingredients we hold. "
            + "That is what the list says, not a guarantee about the product.",

        _ => "There is no ingredient list for this product yet.",
    };
}

/// <summary>
/// Decides what can be said about palm in a product, from the ingredient dictionary.
/// </summary>
/// <remarks>
/// Searching the raw text for "palm" is the obvious implementation and it is wrong in both
/// directions, which is the entire reason the dictionary exists.
///
/// It reports palm that is not there. Vitamin A palmitate is named after palmitic acid and is a
/// trace fortificant; palm sugar is boiled palm sap and has no connection to palm oil at all.
/// Both contain the letters.
///
/// It misses palm that may well be there. The commonest fat wording on an Indian label is
/// "edible vegetable oil", which appears on 635 products in this catalogue and names no oil.
/// A text search finds nothing and the app would report "no palm detected", which is a confident
/// answer we have no basis for. The dictionary records that as unknown instead.
///
/// A quantity is only ever reported when the label prints one. There is no estimating a
/// percentage from ingredient order, which would be inventing a number.
/// </remarks>
public sealed partial class PalmDetection
{
    private const string PalmFlag = "palm_derived";
    private const string MaybePalmFlag = "may_be_palm_derived";

    /// <summary>
    /// Every alias in the dictionary, longest first.
    /// </summary>
    /// <remarks>
    /// Deliberately every entry, not only the palm-flagged ones. Matching runs longest alias
    /// first and each match claims the text it sits on, so an unflagged entry is what stops a
    /// short alias matching inside a longer name: "palm sugar" claims its own text, which is why
    /// the bare alias "palm" cannot report it as palm oil. Only flagged matches are ever
    /// reported; the rest exist purely to hold their ground.
    /// </remarks>
    private readonly IReadOnlyList<(string Alias, IngredientRecord Ingredient)> _aliases;

    public PalmDetection(IEnumerable<IngredientRecord> dictionary)
    {
        _aliases = dictionary
            .SelectMany(i => (i.Aliases ?? [])
                .Append(i.Name)
                .Select(a => (Alias: Normalise(a), Ingredient: i)))
            .Where(p => p.Alias.Length > 0)
            .GroupBy(p => p.Alias, StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderByDescending(p => p.Alias.Length)
            .ToList();
    }

    /// <summary>
    /// Folds label text to a comparable form: lower case, accents removed, remaining punctuation
    /// reduced to single spaces. Indian labels arrive with HTML entities, ampersands and
    /// inconsistent spacing, and none of that changes which ingredient is named.
    /// </summary>
    internal static string Normalise(string text)
    {
        var folded = Fold(text.ToLowerInvariant());
        var cleaned = NonWord().Replace(folded, " ");
        return Whitespace().Replace(cleaned, " ").Trim();
    }

    /// <summary>
    /// Splits a label into the things it names, then normalises each.
    /// </summary>
    /// <remarks>
    /// Separators carry meaning and flattening them invents ingredients. "Edible vegetable oil
    /// (palm), sugar" collapses to "edible vegetable oil palm sugar", in which "palm sugar" now
    /// appears and claims the text, so a packet that plainly says palm reports as not stated.
    /// That is a real Britannia biscuit, found on a device.
    ///
    /// Brackets are separators too, not noise. "Edible vegetable oil (palm)" is a generic name
    /// followed by the specific one, and splitting there is what lets both be matched: the
    /// generic as ambiguous, the specific as definite. 585 products in the catalogue name their
    /// oil this way.
    /// </remarks>
    internal static IReadOnlyList<string> Segments(string text) =>
        [.. Separators().Split(text)
            .Select(Normalise)
            .Where(s => s.Length > 0)];

    /// <summary>
    /// Explicit accent folding. String.Normalize is a no-op under InvariantGlobalization, which
    /// is how "Nestlé" once became "nestl" in the importer, so the table is written out.
    /// </summary>
    private static string Fold(string text)
    {
        Span<char> buffer = text.Length <= 256 ? stackalloc char[text.Length] : new char[text.Length];

        for (var i = 0; i < text.Length; i++)
        {
            buffer[i] = text[i] switch
            {
                'á' or 'à' or 'â' or 'ä' or 'ã' or 'å' => 'a',
                'é' or 'è' or 'ê' or 'ë' => 'e',
                'í' or 'ì' or 'î' or 'ï' => 'i',
                'ó' or 'ò' or 'ô' or 'ö' or 'õ' => 'o',
                'ú' or 'ù' or 'û' or 'ü' => 'u',
                'ç' => 'c',
                'ñ' => 'n',
                var other => other,
            };
        }

        return new string(buffer);
    }

    /// <summary>
    /// Finds what the label says about palm.
    /// </summary>
    /// <param name="ingredientsRaw">The ingredient statement exactly as printed.</param>
    /// <param name="parsed">
    /// The parsed ingredient tree when one exists. Used only for declared percentages, because
    /// a percentage that survived parsing is more trustworthy than one recovered from the text.
    /// </param>
    public PalmFinding Detect(string? ingredientsRaw, IReadOnlyList<Ingredient>? parsed = null)
    {
        if (string.IsNullOrWhiteSpace(ingredientsRaw))
        {
            return new PalmFinding(PalmState.NoIngredients, [], []);
        }

        var definite = new List<PalmMatch>();
        var possible = new List<PalmMatch>();

        foreach (var segment in Segments(ingredientsRaw))
        {
            var claimed = new List<(int Start, int End)>();

            foreach (var (alias, ingredient) in _aliases)
            {
                var index = IndexOfWhole(segment, alias);

                if (index < 0)
                {
                    continue;
                }

                // Longest alias wins the text it sits on, so "palm kernel oil" is not also
                // counted as "palm oil", and the bare word "palm" cannot claim text that
                // "palm sugar" already owns.
                if (claimed.Any(c => index < c.End && index + alias.Length > c.Start))
                {
                    continue;
                }

                claimed.Add((index, index + alias.Length));

                var flags = ingredient.Flags;

                // Unflagged entries claimed their text and say nothing about palm. Holding their
                // ground is their whole purpose here.
                if (flags is null)
                {
                    continue;
                }

                var match = new PalmMatch(
                    ingredient.Id,
                    ingredient.Name,
                    alias,
                    PercentageFor(ingredient, alias, ingredientsRaw, parsed));

                if (flags.Contains(PalmFlag))
                {
                    definite.Add(match);
                }
                else if (flags.Contains(MaybePalmFlag))
                {
                    possible.Add(match);
                }
            }
        }

        if (definite.Count > 0)
        {
            // Sorted so a match carrying a declared percentage is the one reported, since it is
            // the most informative thing we can say.
            definite = [.. definite.OrderByDescending(m => m.Percentage.HasValue)];

            return new PalmFinding(
                definite[0].Percentage is not null ? PalmState.ConfirmedQuantity : PalmState.Present,
                definite,
                possible);
        }

        return possible.Count > 0
            ? new PalmFinding(PalmState.Unknown, [], possible)
            : new PalmFinding(PalmState.NotDetected, [], []);
    }

    /// <summary>
    /// A percentage for this ingredient, only if the label states one.
    /// </summary>
    private static double? PercentageFor(
        IngredientRecord ingredient,
        string alias,
        string raw,
        IReadOnlyList<Ingredient>? parsed)
    {
        if (parsed is not null && FindInTree(parsed, ingredient.Id) is { } fromTree)
        {
            return fromTree;
        }

        // Otherwise look for a percentage printed immediately after the wording, which is how
        // labels state it: "palm oil (25%)" or "palm oil 25%". Nothing further away is used,
        // because a percentage belonging to a different ingredient would be worse than none.
        var pattern = Regex.Escape(alias).Replace("\\ ", "[^a-z0-9]+");
        var match = Regex.Match(
            Normalise(raw),
            pattern + @"[^a-z0-9%]{0,3}(\d{1,3}(?:\.\d+)?)\s*%",
            RegexOptions.IgnoreCase,
            TimeSpan.FromMilliseconds(250));

        return match.Success
               && double.TryParse(match.Groups[1].Value, CultureInfo.InvariantCulture, out var pct)
               && pct is > 0 and <= 100
            ? pct
            : null;
    }

    private static double? FindInTree(IReadOnlyList<Ingredient> items, string id)
    {
        foreach (var item in items)
        {
            if (string.Equals(item.Canonical, id, StringComparison.Ordinal) && item.Percentage is { } p)
            {
                return p;
            }

            if (item.Children is { } children && FindInTree(children, id) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>
    /// Finds an alias only where it stands as whole words, so "palm oil" does not match inside
    /// "palm oilseed" and "shortening" does not match inside a longer word.
    /// </summary>
    private static int IndexOfWhole(string haystack, string needle)
    {
        var from = 0;

        while (from <= haystack.Length - needle.Length)
        {
            var index = haystack.IndexOf(needle, from, StringComparison.Ordinal);

            if (index < 0)
            {
                return -1;
            }

            var beforeOk = index == 0 || haystack[index - 1] == ' ';
            var after = index + needle.Length;
            var afterOk = after == haystack.Length || haystack[after] == ' ';

            if (beforeOk && afterOk)
            {
                return index;
            }

            from = index + 1;
        }

        return -1;
    }

    [GeneratedRegex(@"[^a-z0-9%. ]+")]
    private static partial Regex NonWord();

    /// <summary>
    /// What separates one named thing from the next on a label: list punctuation, brackets and
    /// line breaks. Ampersands and hyphens are absent on purpose, because they sit inside names
    /// such as "refined palm &amp; palmolein oil".
    /// </summary>
    [GeneratedRegex(@"[,;()\[\]{}•|
]+")]
    private static partial Regex Separators();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
