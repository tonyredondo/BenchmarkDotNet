﻿using System;
using System.Linq;
using System.Collections.Generic;
using BenchmarkDotNet.Analysers;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Validators;
using BenchmarkDotNet.Extensions;

namespace BenchmarkDotNet.Configs
{
    public class ConfigParser
    {
        private class ConfigOption
        {
            public Action<ManualConfig, string> ProcessOption { get; set; } = (config, value) => { };
            public Action<ManualConfig> ProcessAllOptions { get; set; } = (config) => { };
            public Lazy<IEnumerable<string>> GetAllOptions { get; set; } = new Lazy<IEnumerable<string>>(() => Enumerable.Empty<string>());
        }

        // NOTE: GetAllOptions needs to be Lazy<T>, because they call static variables (and then the initialisation order is tricky!!)
        private static Dictionary<string, ConfigOption> configuration = new Dictionary<string, ConfigOption>
        {
            { "jobs", new ConfigOption {
                ProcessOption = (config, value) => config.Add(ParseItem("Job", availableJobs, value)),
                ProcessAllOptions = (config) => config.Add(allJobs.Value),
                GetAllOptions = new Lazy<IEnumerable<string>>(() => availableJobs.Keys)
            } },
            { "columns", new ConfigOption {
                ProcessOption = (config, value) => config.Add(ParseItem("Column", availableColumns, value)),
                ProcessAllOptions = (config) => config.Add(allColumns.Value),
                GetAllOptions = new Lazy<IEnumerable<string>>(() => availableColumns.Keys)
            } },
            { "exporters", new ConfigOption {
                ProcessOption = (config, value) => config.Add(ParseItem("Exporter", availableExporters, value)),
                ProcessAllOptions = (config) => config.Add(allExporters.Value),
                GetAllOptions = new Lazy<IEnumerable<string>>(() => availableExporters.Keys)
            } },
            { "diagnosers", new ConfigOption {
                ProcessOption = (config, value) => config.Add(ParseDiagnosers(value)),
                // TODO these 2 should match the lookup in LoadDiagnosers() in DefaultConfig.cs
                GetAllOptions = new Lazy<IEnumerable<string>>(() => Enumerable.Empty<string>())
            } },
            { "analysers", new ConfigOption {
                ProcessOption = (config, value) => config.Add(ParseItem("Analyser", availableAnalysers, value)),
                GetAllOptions = new Lazy<IEnumerable<string>>(() => availableAnalysers.Keys)
            } },
            { "validators", new ConfigOption {
                ProcessOption = (config, value) => config.Add(ParseItem("Validator", availableValidators, value)),
                GetAllOptions = new Lazy<IEnumerable<string>>(() => availableValidators.Keys)
            } },
            { "loggers", new ConfigOption {
                // TODO does it make sense to allows Loggers to be configured on the cmd-line?
                ProcessOption = (config, value) => { throw new InvalidOperationException($"{value} is an unrecognised Logger"); },
            } },
        };

        private static Dictionary<string, IJob[]> availableJobs =
            new Dictionary<string, IJob[]>
            {
                { "default", new [] { Job.Default } },
                { "legacyjitx86", new[] { Job.LegacyJitX86 } },
                { "legacyjitx64", new[] { Job.LegacyJitX64 } },
                { "ryujitx64", new[] { Job.RyuJitX64 } },
                { "dry", new[] { Job.Dry } },
                { "alljits", Job.AllJits },
                { "clr", new[] { Job.Clr } },
                { "mono", new[] { Job.Mono } },
                { "longrun", new[] { Job.LongRun } }
            };
        private static Lazy<IJob[]> allJobs = new Lazy<IJob[]>(() => availableJobs.SelectMany(e => e.Value).ToArray());

        private static Dictionary<string, IColumn[]> availableColumns =
            new Dictionary<string, IColumn[]>
            {
                { "mean", new [] { StatisticColumn.Mean } },
                { "stderror", new[] { StatisticColumn.StdError } },
                { "stddev", new[] { StatisticColumn.StdDev } },
                { "operationpersecond", new [] { StatisticColumn.OperationsPerSecond } },
                { "min", new[] { StatisticColumn.Min } },
                { "q1", new[] { StatisticColumn.Q1 } },
                { "median", new[] { StatisticColumn.Median } },
                { "q3", new[] { StatisticColumn.Q3 } },
                { "max", new[] { StatisticColumn.Max } },
                { "allstatistics", StatisticColumn.AllStatistics  },
                { "place", new[] { PlaceColumn.ArabicNumber } }
            };
        private static Lazy<IColumn[]> allColumns = new Lazy<IColumn[]>(() => availableColumns.SelectMany(e => e.Value).ToArray());

        private static Dictionary<string, IExporter[]> availableExporters =
            new Dictionary<string, IExporter[]>
            {
                { "csv", new [] { CsvExporter.Default } },
                { "csvmeasurements", new[] { CsvMeasurementsExporter.Default } },
                { "html", new[] { HtmlExporter.Default } },
                { "markdown", new [] { MarkdownExporter.Default } },
                { "stackoverflow", new[] { MarkdownExporter.StackOverflow } },
                { "github", new[] { MarkdownExporter.GitHub } },
                { "plain", new[] { PlainExporter.Default } },
                { "rplot", new[] { RPlotExporter.Default } },
                { "json", new[] { JsonExporter.Default } },
                { "briefjson", new[] { BriefJsonExporter.Default } },
                { "formattedjson", new[] { FormattedJsonExporter.Default } },
            };
        private static Lazy<IExporter[]> allExporters = new Lazy<IExporter[]>(() => availableExporters.SelectMany(e => e.Value).ToArray());

        private static Dictionary<string, IAnalyser[]> availableAnalysers =
            new Dictionary<string, IAnalyser[]>
            {
                { "environment", new [] { EnvironmentAnalyser.Default } }
            };

        private static Dictionary<string, IValidator[]> availableValidators =
            new Dictionary<string, IValidator[]>
            {
                { "baseline", new [] { BaselineValidator.FailOnError } },
                { "jitOptimizations", new [] { JitOptimizationsValidator.DontFailOnError } },
                { "jitOptimizationsFailOnError", new [] { JitOptimizationsValidator.FailOnError } },
            };

        public IConfig Parse(string[] args)
        {
            var config = new ManualConfig();
            foreach (var arg in args.Where(arg => arg.Contains("=")))
            {
                var split = arg.ToLowerInvariant().Split('=');
                var values = split[1].Split(',');
                // Delibrately allow both "job" and "jobs" to be specified, makes it easier for users!!
                var argument = split[0].EndsWith("s") ? split[0] : split[0] + "s";
                // Allow both "--arg=<value>" and "arg=<value>" (i.e. with and without the double dashes)
                argument = argument.StartsWith(optionPrefix) ? argument.Remove(0, 2) : argument;

                if (configuration.ContainsKey(argument) == false)
                    continue;

                if (values.Length == 1 && values[0] == "all")
                {
                    configuration[argument].ProcessAllOptions(config);
                }
                else
                {
                    var processOption = configuration[argument].ProcessOption;
                    foreach (var value in values)
                        processOption(config, value);
                }
            }
            return config;
        }

        // TODO also consider allowing short version (i.e. '-d' and '--diagnosers')
        private string optionPrefix = "--";
        private char[] trimChars = new[] { ' ' };
        private const string breakText = ": ";

        public void PrintOptions(ILogger logger, int prefixWidth, int outputWidth)
        {
            foreach (var option in configuration)
            {
                var optionText = $"  {optionPrefix}{option.Key} <{option.Key.ToUpperInvariant()}>";
                logger.WriteResult($"{optionText.PadRight(prefixWidth)}");

                var parameters = string.Join(", ", option.Value.GetAllOptions.Value);
                var explanation = $"Allowed values: ";
                logger.WriteInfo($": {explanation}");

                var maxWidth = outputWidth - prefixWidth - explanation.Length - Environment.NewLine.Length - breakText.Length;
                var lines = StringExtensions.Wrap(parameters, maxWidth);
                if (lines.Count == 0)
                {
                    logger.WriteLine();
                    continue;
                }

                logger.WriteLineInfo($"{lines.First().Trim(trimChars)}");
                var padding = new string(' ', prefixWidth);
                var explanationPadding = new string(' ', explanation.Length);
                foreach (var line in lines.Skip(1))
                    logger.WriteLineInfo($"{padding}{breakText}{explanationPadding}{line.Trim(trimChars)}");
            }
        }

        private static T[] ParseItem<T>(string itemName, Dictionary<string, T[]> itemLookup, string value)
        {
            if (itemLookup.ContainsKey(value))
                return itemLookup[value];

            throw new InvalidOperationException($"\"{value}\" is an unrecognised {itemName}");
        }

        private static IDiagnoser[] ParseDiagnosers(string value)
        {
            foreach (var diagnoser in DefaultConfig.LazyLoadedDiagnosers.Value)
            {
                if (value == diagnoser.GetType().Name.Replace("Diagnoser", "").ToLowerInvariant())
                    return new[] { diagnoser };
            }
            throw new InvalidOperationException($"{value} is an unrecognised Diagnoser");
        }
    }
}