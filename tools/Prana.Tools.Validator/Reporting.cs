using System.Text;
using System.Text.Json;

namespace Prana.Tools.Validator;

/// <summary>How findings are printed.</summary>
public enum ReportFormat
{
    /// <summary>For a person at a terminal.</summary>
    Human,

    /// <summary>GitHub workflow commands, which become annotations on the diff.</summary>
    GitHub,

    /// <summary>Machine readable, for later workflows that need to act on findings.</summary>
    Json,
}

/// <summary>Prints findings in whichever form the caller asked for.</summary>
public static class Reporting
{
    public static void Write(
        TextWriter output,
        ReportFormat format,
        IReadOnlyList<Diagnostic> diagnostics,
        int filesChecked,
        TimeSpan elapsed,
        bool strict)
    {
        switch (format)
        {
            case ReportFormat.GitHub:
                WriteGitHub(output, diagnostics, filesChecked, elapsed, strict);
                break;

            case ReportFormat.Json:
                WriteJson(output, diagnostics, filesChecked, elapsed);
                break;

            default:
                WriteHuman(output, diagnostics, filesChecked, elapsed, strict);
                break;
        }
    }

    private static void WriteHuman(
        TextWriter output,
        IReadOnlyList<Diagnostic> diagnostics,
        int filesChecked,
        TimeSpan elapsed,
        bool strict)
    {
        foreach (var group in diagnostics.GroupBy(d => d.File).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            output.WriteLine();
            output.WriteLine(group.Key);

            foreach (var diagnostic in group.OrderByDescending(d => d.Severity).ThenBy(d => d.Line))
            {
                var where = diagnostic.Line > 0 ? $"{diagnostic.Line}:{diagnostic.Column}" : "-";
                output.WriteLine($"  {where,-9} {Label(diagnostic.Severity),-7} {diagnostic.Code}  {diagnostic.Message}");

                if (diagnostic.Pointer.Length > 0)
                {
                    output.WriteLine($"  {string.Empty,-9} {string.Empty,-7} {string.Empty,7}  at {diagnostic.Pointer}");
                }
            }
        }

        output.WriteLine();
        output.WriteLine(Summary(diagnostics, filesChecked, elapsed, strict));
    }

    private static void WriteGitHub(
        TextWriter output,
        IReadOnlyList<Diagnostic> diagnostics,
        int filesChecked,
        TimeSpan elapsed,
        bool strict)
    {
        foreach (var diagnostic in diagnostics)
        {
            // Info findings are deliberately not annotated. A pull request covered in grey notes
            // trains people to ignore all of them, including the ones that matter.
            if (diagnostic.Severity == Severity.Info)
            {
                continue;
            }

            var command = diagnostic.Severity switch
            {
                Severity.Error => "error",
                _ => strict ? "error" : "warning",
            };

            var builder = new StringBuilder("::").Append(command)
                .Append(" file=").Append(EscapeProperty(diagnostic.File));

            if (diagnostic.Line > 0)
            {
                builder.Append(",line=").Append(diagnostic.Line)
                       .Append(",col=").Append(diagnostic.Column);
            }

            builder.Append(",title=").Append(EscapeProperty(diagnostic.Code))
                   .Append("::").Append(EscapeMessage(diagnostic.Message));

            output.WriteLine(builder.ToString());
        }

        output.WriteLine(Summary(diagnostics, filesChecked, elapsed, strict));
    }

    private static void WriteJson(
        TextWriter output,
        IReadOnlyList<Diagnostic> diagnostics,
        int filesChecked,
        TimeSpan elapsed)
    {
        var payload = new
        {
            filesChecked,
            elapsedMs = (int)elapsed.TotalMilliseconds,
            counts = new
            {
                error = diagnostics.Count(d => d.Severity == Severity.Error),
                warning = diagnostics.Count(d => d.Severity == Severity.Warning),
                info = diagnostics.Count(d => d.Severity == Severity.Info),
            },
            diagnostics = diagnostics.Select(d => new
            {
                severity = Label(d.Severity),
                code = d.Code,
                message = d.Message,
                file = d.File,
                pointer = d.Pointer,
                line = d.Line,
                column = d.Column,
            }),
        };

        output.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static string Summary(
        IReadOnlyList<Diagnostic> diagnostics,
        int filesChecked,
        TimeSpan elapsed,
        bool strict)
    {
        var errors = diagnostics.Count(d => d.Severity == Severity.Error);
        var warnings = diagnostics.Count(d => d.Severity == Severity.Warning);
        var infos = diagnostics.Count(d => d.Severity == Severity.Info);

        var verdict = errors > 0 || (strict && warnings > 0) ? "FAILED" : "passed";

        return $"{verdict}: {filesChecked} file(s) checked in {elapsed.TotalSeconds:0.00}s. "
            + $"{errors} error(s), {warnings} warning(s), {infos} note(s)."
            + (strict ? " Warnings count as errors (--strict)." : string.Empty);
    }

    private static string Label(Severity severity) => severity switch
    {
        Severity.Error => "error",
        Severity.Warning => "warning",
        _ => "note",
    };

    // GitHub parses these lines, so any character that would end a property or the command
    // itself has to be encoded or the annotation silently lands in the wrong place.
    private static string EscapeProperty(string value) => EscapeMessage(value)
        .Replace(":", "%3A", StringComparison.Ordinal)
        .Replace(",", "%2C", StringComparison.Ordinal);

    private static string EscapeMessage(string value) => value
        .Replace("%", "%25", StringComparison.Ordinal)
        .Replace("\r", "%0D", StringComparison.Ordinal)
        .Replace("\n", "%0A", StringComparison.Ordinal);
}
