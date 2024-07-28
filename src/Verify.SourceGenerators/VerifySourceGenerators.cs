﻿using Microsoft.CodeAnalysis;

namespace VerifyTests;

public static class VerifySourceGenerators
{
    public static bool Initialized { get; private set; }

    public static void Initialize()
    {
        if (Initialized)
        {
            throw new("Already Initialized");
        }

        Initialized = true;

        InnerVerifier.ThrowIfVerifyHasBeenRun();
        VerifierSettings.AddExtraSettings(serializer =>
        {
            var converters = serializer.Converters;
            converters.Add(new LocalizableStringConverter());
            converters.Add(new DiagnosticConverter());
            converters.Add(new LocationConverter());
            converters.Add(new GeneratedSourceResultConverter());
            converters.Add(new DiagnosticDescriptorConverter());
            converters.Add(new SourceTextConverter());
        });
        VerifierSettings.RegisterFileConverter<GeneratorDriver>(Convert);
        VerifierSettings.RegisterFileConverter<GeneratorDriverRunResult>(Convert);
    }

    static ConversionResult Convert(GeneratorDriverRunResult target, IReadOnlyDictionary<string, object> context)
    {
        var exceptions = new List<Exception>();
        var targets = new List<Target>();
        var ignoreResults = GetIgnoreResults(context);

        foreach (var result in target.Results)
        {
            if (result.Exception != null)
            {
                exceptions.Add(result.Exception);
            }

            var collection = result.GeneratedSources
                .Where(source => ignoreResults == null ||
                                 !ignoreResults.Any(_ => _(source)))
                .OrderBy(_ => _.HintName)
                .Select(SourceToTarget);
            targets.AddRange(collection);
        }

        if (exceptions.Count == 1)
        {
            throw exceptions.First();
        }

        if (exceptions.Count > 1)
        {
            throw new AggregateException(exceptions);
        }

        if (target.Diagnostics.Any())
        {
            var info = new
            {
                target.Diagnostics
            };
            return new(info, targets);
        }

        return new(null, targets);
    }

    static List<Func<GeneratedSourceResult, bool>>? GetIgnoreResults(IReadOnlyDictionary<string, object> context)
    {
        if (context.TryGetValue(IgnoreContextName, out var value))
        {
            return (List<Func<GeneratedSourceResult, bool>>)value;
        }

        return null;
    }

    static Target SourceToTarget(GeneratedSourceResult source)
    {
        var data = $"""
                    //HintName: {source.HintName}
                    {source.SourceText}
                    """;
        return new("cs", data, Path.GetFileNameWithoutExtension(source.HintName));
    }

    static ConversionResult Convert(GeneratorDriver target, IReadOnlyDictionary<string, object> context) =>
        Convert(target.GetRunResult(), context);

    internal const string IgnoreContextName = $"{nameof(VerifySourceGenerators)}.{nameof(IgnoreGeneratedResult)}";

    public static SettingsTask IgnoreGeneratedResult(this SettingsTask settingsTask, Func<GeneratedSourceResult, bool> shouldIgnore)
    {
        settingsTask.CurrentSettings.IgnoreGeneratedResult(shouldIgnore);
        return settingsTask;
    }

    public static void IgnoreGeneratedResult(this VerifySettings verifySettings, Func<GeneratedSourceResult, bool> shouldIgnore)
    {
        if (!verifySettings.Context.TryGetValue(IgnoreContextName, out var value))
        {
            value = new List<Func<GeneratedSourceResult, bool>>();
            verifySettings.Context.Add(IgnoreContextName, value);
        }

        if (value is not List<Func<GeneratedSourceResult, bool>> ignoreList)
        {
            throw new($"Unexpected value in {nameof(verifySettings.Context)}, type is not {nameof(List<Func<GeneratedSourceResult, bool>>)}");
        }

        ignoreList.Add(shouldIgnore);
    }
}