using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace DeepElevator;

/// <summary>
/// Step A probe: accumulates how long our code spends in named sections, so a frame hitch can be
/// attributed to us or ruled out. Cheap enough to leave on every frame; the statistics are only
/// printed by <see cref="DumpAndReset"/>; the hitch detector clears them every frame, so a dump covers one frame.
/// </summary>
public static class ElevatorProfiler
{
    private class SectionStatistics
    {
        public int CallCount;
        public double TotalMilliseconds;
        public double MaxMilliseconds;
        public string LastNote;
    }

    private static readonly Dictionary<string, SectionStatistics> statisticsBySection = new Dictionary<string, SectionStatistics>();

    /// <summary>Times the section until disposed. Use as: <c>using (ElevatorProfiler.Measure("name")) { ... }</c></summary>
    public static Scope Measure(string sectionName) => new Scope(sectionName);

    /// <summary>Attaches a free-text note (for example the last result) to a section; shown in the dump.</summary>
    public static void Note(string sectionName, string note)
    {
        GetStatistics(sectionName).LastNote = note;
    }

    /// <summary>True if any section was measured since the last reset.</summary>
    public static bool HasStatistics => statisticsBySection.Count > 0;

    /// <summary>How often a section was measured since the last reset (0 if never).</summary>
    public static int GetCallCount(string sectionName)
    {
        return statisticsBySection.TryGetValue(sectionName, out SectionStatistics statistics) ? statistics.CallCount : 0;
    }

    /// <summary>Discards the statistics without formatting them.</summary>
    public static void Reset() => statisticsBySection.Clear();

    /// <summary>Formats every section measured since the last dump, then clears them.</summary>
    public static string DumpAndReset()
    {
        if (statisticsBySection.Count == 0)
        {
            return "no DeepElevator code ran since the last dump";
        }

        var text = new StringBuilder();
        foreach (KeyValuePair<string, SectionStatistics> entry in statisticsBySection)
        {
            SectionStatistics statistics = entry.Value;
            text.Append($"\n    {entry.Key}: calls={statistics.CallCount} total={statistics.TotalMilliseconds:F3}ms max={statistics.MaxMilliseconds:F3}ms");
            if (statistics.LastNote != null)
            {
                text.Append($" last=[{statistics.LastNote}]");
            }
        }

        statisticsBySection.Clear();
        return text.ToString();
    }

    private static SectionStatistics GetStatistics(string sectionName)
    {
        if (!statisticsBySection.TryGetValue(sectionName, out SectionStatistics statistics))
        {
            statistics = new SectionStatistics();
            statisticsBySection[sectionName] = statistics;
        }
        return statistics;
    }

    private static void Record(string sectionName, double elapsedMilliseconds)
    {
        SectionStatistics statistics = GetStatistics(sectionName);
        statistics.CallCount++;
        statistics.TotalMilliseconds += elapsedMilliseconds;
        if (elapsedMilliseconds > statistics.MaxMilliseconds)
        {
            statistics.MaxMilliseconds = elapsedMilliseconds;
        }
    }

    /// <summary>Stopwatch scope; a struct so measuring does not allocate.</summary>
    public readonly struct Scope : IDisposable
    {
        private readonly string sectionName;
        private readonly long startTimestamp;

        public Scope(string sectionName)
        {
            this.sectionName = sectionName;
            startTimestamp = Stopwatch.GetTimestamp();
        }

        public void Dispose()
        {
            double elapsedMilliseconds = (Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / Stopwatch.Frequency;
            Record(sectionName, elapsedMilliseconds);
        }
    }
}
