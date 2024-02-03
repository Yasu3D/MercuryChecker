using Avalonia.Controls;
using Avalonia.Input;
using System.Linq;
using System.IO;
using MercuryChecker.ChartData;
using System;
using SkiaSharp;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Threading;
using System.Collections.Generic;

namespace MercuryChecker;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        AddHandler(DragDrop.DropEvent, MainWindow_Drop);
    }

    private SkiaRenderEngine renderEngine = new();
    private Chart chart = new();

    private Uri filepath;
    private string filename = "";

    private string notesInvalidString = "";
    private string notesOverlapString = "";
    private string notesSmallString = "";
                  
    private string holdsInvalidString = "";
    private string holdsUnbakedString = "";
    private string holdsTooFastString = "";
                  
    private string timingOddString = "";
    private string timingTooFastString = "";
                  
    private string playabilityVisionString = "";
    private string playabilityMovementString = "";
    private string playabilityTopHeavyString = "";
                  
    private string statsNoteCountString = "";
    private string statsNpsString = "";
    private string statsSkillString = "";
    private string statsLevelString = "";
                  
    private string resultText = "";

    private float[] heatmapWeights = new float[60];
    private float[] skillRadarValues = [0, 0, 0];

    private enum MessageType
    {
        None = 0,
        Suggestion = 1,
        Warning = 2,
        Error = 3,
        Debug = 4
    }

    /// <summary>
    /// Sends stuff to RenderEngine to render the note heatmap.
    /// </summary>
    private void RenderHeatmap(SKCanvas canvas)
    {
        renderEngine.RenderHeatmap(canvas, heatmapWeights);
    }

    /// <summary>
    /// Sends stuff to RenderEngine to render the skill triangle.
    /// </summary>
    private void RenderSkillRadar(SKCanvas canvas)
    {
        renderEngine.RenderSkillRadar(canvas, skillRadarValues);
    }

    /// <summary>
    /// Handles Drag & Drop of Files
    /// </summary>
    private void MainWindow_Drop(object? sender, DragEventArgs e)
    {
        var path = e.Data.GetFiles()?.First().Path;
        if (path == null || TestResults.Inlines == null) return;

        filepath = path;
        chart.ParseFile(path);
        filename = Path.GetFileName(Uri.UnescapeDataString(path.LocalPath));

        WriteResults();

        e.Handled = true;
    }

    /// <summary>
    /// Adds a message to TestResults.
    /// </summary>
    private void AddMessage(string message, MessageType messageType = MessageType.None)
    {
        if (TestResults.Inlines is null) return;

        switch (messageType)
        {
            case MessageType.Suggestion:
                Dispatcher.UIThread.Invoke(() => TestResults.Inlines.Add(new Run("SUGGESTION:  ") { FontWeight = FontWeight.Bold, Foreground = Brushes.Turquoise }));
                break;

            case MessageType.Warning:
                Dispatcher.UIThread.Invoke(() => TestResults.Inlines.Add(new Run("WARNING:  ") { FontWeight = FontWeight.Bold, Foreground = Brushes.Orange }));
                break;

            case MessageType.Error:
                Dispatcher.UIThread.Invoke(() => TestResults.Inlines.Add(new Run("ERROR:  ") { FontWeight = FontWeight.Bold, Foreground = Brushes.Red }));
                break;

            case MessageType.Debug:
                Dispatcher.UIThread.Invoke(() => TestResults.Inlines.Add(new Run("DEBUG:  ") { FontWeight = FontWeight.Bold, Foreground = Brushes.Blue }));
                break;
        }

        Dispatcher.UIThread.Invoke(() => TestResults.Inlines.Add(message));
    }

    /// <summary>
    /// Removes all messages from TestResults.
    /// </summary>
    private void RemoveMessages()
    {
        TestResults.Inlines?.Clear();
        TestResults.Inlines?.Add(new Run("")); // I hate this but it makes it work
    }

    /// <summary>
    /// Runs algorithms and adds messages to TestResults.
    /// </summary>
    private void WriteResults()
    {
        if (TestResults.Inlines == null) return;

        RemoveMessages();

        if (!chart.loaded)
        {
            TestResults.Inlines.Add("Invalid file!");
            HeatmapCanvas.IsVisible = false;
            SkillRadarCanvas.IsVisible = false;
            return;
        }

        AddMessage($"{filename}\n\n", MessageType.None);

        GetGeneralErrors();

        if (ShowNotesInvalid.IsChecked != null && (bool)ShowNotesInvalid.IsChecked)
            GetNotesInvalid();

        if (ShowNotesOverlap.IsChecked != null && (bool)ShowNotesOverlap.IsChecked)
            GetNotesOverlap();

        if (ShowNotesSmall.IsChecked != null && (bool)ShowNotesSmall.IsChecked)
            GetNotesSmall();

        if (ShowHoldsInvalid.IsChecked != null && (bool)ShowHoldsInvalid.IsChecked)
            GetHoldsInvalid();

        if (ShowHoldsUnbaked.IsChecked != null && (bool)ShowHoldsUnbaked.IsChecked)
            GetHoldsUnbaked();

        if (ShowPlayabilityEBpm.IsChecked != null && (bool)ShowPlayabilityEBpm.IsChecked)
            GetHighEBpm();

        if (ShowPlayabilityVision.IsChecked != null && (bool)ShowPlayabilityVision.IsChecked)
            GetVisionBlocks();

        if (ShowStatsCounts.IsChecked != null && (bool)ShowStatsCounts.IsChecked)
            GetNoteCount();

        if (ShowStatsNPS.IsChecked != null && (bool)ShowStatsNPS.IsChecked)
            GetNPS();

        if (ShowStatsLevel.IsChecked != null && (bool)ShowStatsLevel.IsChecked)
            GetLevel();

        if (ShowStatsHeatmap.IsChecked != null && (bool)ShowStatsHeatmap.IsChecked)
            GetHeatmapValues();

        if (ShowStatsSkill.IsChecked != null && (bool)ShowStatsSkill.IsChecked)
            GetSkillRadarValues();

        HeatmapGroup.IsVisible = ShowStatsHeatmap.IsChecked ?? false;
        SkillTriangleGroup.IsVisible = ShowStatsSkill.IsChecked ?? false;

        if (ShowDebugParity.IsChecked != null && (bool)ShowDebugParity.IsChecked)
            GetParity();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.R)
        {
            if (filepath is null) return;

            chart.ParseFile(filepath);
            WriteResults();
        }

        e.Handled = true;
    }

    private void GetGeneralErrors()
    {
        AddMessage($"—————— General Checks ——————————\n");

        int endChartCount = chart.notes.Count(x => x.NoteType is Enums.NoteType.EndChart);
        switch (endChartCount)
        {
            case 0: AddMessage($"No EndOfChart note found!\n", MessageType.Error);
                break;

            case 1: break;

            default: AddMessage($"This chart has more than one [{endChartCount}] EndOfChart notes!\n", MessageType.Error);
                break;
        }

        if (chart.notes.Last().NoteType is not Enums.NoteType.EndChart)
            AddMessage($"Last note is not EndOfChart!\n", MessageType.Error);

        Enums.GimmickType lastReverse = Enums.GimmickType.None;

        for (int i = 0; i < chart.reverseGimmicks.Count; i++)
        {
            var current = chart.reverseGimmicks[i];

            if ((lastReverse == Enums.GimmickType.ReverseEffectStart && current.GimmickType != Enums.GimmickType.ReverseEffectEnd) ||
                (lastReverse == Enums.GimmickType.ReverseEffectEnd   && current.GimmickType != Enums.GimmickType.ReverseNoteEnd)   ||
                (lastReverse == Enums.GimmickType.ReverseNoteEnd     && current.GimmickType != Enums.GimmickType.ReverseEffectStart))
                AddMessage($"Invalid {current.GimmickType} @ {current.Measure} {current.Tick}\n", MessageType.Error);

            lastReverse = current.GimmickType;
        }


        Enums.GimmickType lastStop = Enums.GimmickType.None;

        for (int i = 0; i < chart.stopGimmicks.Count; i++)
        {
            var current = chart.stopGimmicks[i];

            if ((lastStop == Enums.GimmickType.StopStart && current.GimmickType != Enums.GimmickType.StopEnd) ||
                (lastStop == Enums.GimmickType.StopEnd && current.GimmickType != Enums.GimmickType.StopStart))
                AddMessage($"Invalid {current.GimmickType} @ {current.Measure} {current.Tick}", MessageType.Error);

            lastStop = current.GimmickType;
        }

        AddMessage("\n");
    }

    private void GetNotesInvalid()
    {
        AddMessage($"—————— Invalid Notes ——————————\n");

        foreach (Note note in chart.objects)
        {
            if (note.Size is < 1 or > 60)
                AddMessage($"{note.NoteType} with invalid size ({note.Size}) @ {note.Measure} {note.Tick}\n", MessageType.Error);

            if (note.Position is < 0 or > 59)
                AddMessage($"{note.NoteType} with invalid position ({note.Position}) @ {note.Measure} {note.Tick}\n", MessageType.Error);

            if (note.NoteType is Enums.NoteType.None)
                AddMessage($"Invalid NoteType @ {note.Measure} {note.Tick}\n", MessageType.Error);

            if (note.Measure < 0 || note.Tick < 0)
                AddMessage($"{note.NoteType} with negative time ({note.Measure} // {note.Tick})\n", MessageType.Error);
        }

        AddMessage("\n");
    }

    private void GetNotesSmall()
    {
        AddMessage($"—————— Small Notes ——————————\n");

        foreach (Note note in chart.notes)
        {
            if ((!note.IsHold && note.Size < 10) || (note.IsSwipe && note.Size < 12))
                AddMessage($"{note.NoteType} with small size ({note.Size}) @ {note.Measure} {note.Tick}\n", MessageType.Suggestion);

            else if (!note.IsHold && note.Size < 7)
                AddMessage($"{note.NoteType} with small size ({note.Size}) @ {note.Measure} {note.Tick}\n", MessageType.Warning);
        }

        AddMessage("\n");
    }

    private void GetNotesOverlap()
    {
        AddMessage($"—————— Overlapping Notes ——————————\n");

        Dictionary<int, Note> checkedNotes = [];

        // i = 1 to avoid ArrayIndexOutOfBounds
        for (int i = 0; i < chart.notes.Count; i++)
        {
            if (chart.notes[i].IsHold) continue;
            if (checkedNotes.ContainsKey(i)) continue;

            var current = chart.notes[i];

            for (int j = i + 1; j < chart.notes.Count; j++)
            {
                var next = chart.notes[j];
                if (next.IsHold) continue;
                if (next.Measure != current.Measure || next.Tick != current.Tick) break;

                checkedNotes[j] = next;

                int note0Start = current.Position;
                int note0End = current.Position + current.Size;

                int note1Start = next.Position;
                int note1End = next.Position + next.Size;

                bool overlapFull = note0Start <= note1Start && note0End >= note1End;
                bool overlapLeft = note1Start > note0Start && note1Start < note0End && note1End > note0End;
                bool overlapRight = note1Start < note0Start && note1End > note0Start && note1End < note0End;

                if (overlapFull)
                {
                    AddMessage($"{next.NoteType} fully overlaps with {current.NoteType} @ {next.Measure} {next.Tick}\n", MessageType.Error);
                    continue;
                }

                if (overlapLeft || overlapRight)
                {
                    System.Diagnostics.Debug.WriteLine($"{note0Start} {note0End} {note1Start} {note1End}");
                    AddMessage($"{next.NoteType} partially overlaps with {current.NoteType} @ {next.Measure} {next.Tick}\n", MessageType.Warning);
                    continue;
                }
            }
        }

        AddMessage("\n");
    }

    private void GetHoldsInvalid()
    {
        AddMessage($"—————— Invalid Holds ——————————\n");

        foreach (Note note in chart.notes)
        {
            if (note.NoteType is Enums.NoteType.HoldStart or Enums.NoteType.HoldSegment && note.NextReferencedNote is null)
            {
                AddMessage($"{note.NoteType} without reference @ {note.Measure} {note.Tick}\n", MessageType.Error);
            }
        }

        AddMessage("\n");
    }

    private void GetHoldsUnbaked()
    {
        AddMessage($"—————— Unbaked Holds ——————————\n");

        foreach (Note note in chart.notes)
        {
            if (!note.IsHold || note.NoteType is Enums.NoteType.HoldEnd) continue;

            var nextNote = note.NextReferencedNote;
            if (nextNote is null) return;

            int sizeDifference = nextNote.Size - note.Size;
            int positionChange = Utils.Modulo(nextNote.Position - note.Position, 60);
            positionChange -= positionChange >= 45 ? 60 : 0;
            positionChange = positionChange > 30 ? -(60 - positionChange) : positionChange;

            if (int.Abs(sizeDifference) > 2 || int.Abs(positionChange) >= 2)
                AddMessage($"Unbaked {note.NoteType} @ {note.Measure} {note.Tick}\n", MessageType.Warning);
        }

        AddMessage("\n");
    }

    private void GetNoteCount()
    {
        if (chart is null) return;

        var tapNotes = chart.notes.Where(x => x.NoteType is Enums.NoteType.Touch).Count();
        var chainNotes = chart.notes.Where(x => x.NoteType is Enums.NoteType.Chain).Count();
        var holdNotes = chart.notes.Where(x => x.NoteType is Enums.NoteType.HoldStart).Count();
        var cwSwipeNotes = chart.notes.Where(x => x.NoteType is Enums.NoteType.SwipeClockwise).Count();
        var ccwSwipeNotes = chart.notes.Where(x => x.NoteType is Enums.NoteType.SwipeCounterclockwise).Count();
        var fwSnapNotes = chart.notes.Where(x => x.NoteType is Enums.NoteType.SnapForward).Count();
        var bwSnapNotes = chart.notes.Where(x => x.NoteType is Enums.NoteType.SnapBackward).Count();

        statsNoteCountString =
            $"—————— Note Counts ——————————\n" +
            $"Tap Notes  : {tapNotes}\n\n" +
            $"Chain Notes: {chainNotes}\n\n" +
            $"Hold Notes : {holdNotes}\n\n" +
            $"Swipe Notes: {cwSwipeNotes + ccwSwipeNotes}\n" +
            $"  Clockwise        : {cwSwipeNotes}\n" +
            $"  CounterClockwise : {ccwSwipeNotes}\n\n" +
            $"Snap Notes : {fwSnapNotes + bwSnapNotes}\n" +
            $"  Forwards         : {fwSnapNotes}\n" +
            $"  Backwards        : {bwSnapNotes}\n";

        AddMessage(statsNoteCountString, MessageType.None);
        AddMessage("\n");
    }

    private void GetNPS()
    {
        int noteCount = chart.nonSegmentNotes.Count;
        int noteWithoutChainCount = chart.nonSegmentNotes.Count(x => x.NoteType is not Enums.NoteType.Chain); 
        float chartDuration = chart.nonSegmentNotes.Last().Time;
        float chartDurationNoBreaks = chartDuration;

        const float pauseThreshold = 3000; // ms

        for (int i = 0; i < noteCount - 1; i++)
        {
            var note = chart.nonSegmentNotes[i];
            var next = chart.nonSegmentNotes[i + 1];

            if (next.Time - note.Time > pauseThreshold)
                chartDurationNoBreaks -= next.Time - note.Time;
        }

        float nps = noteCount / chartDuration * 1000;
        float npsNoChain = noteWithoutChainCount / chartDuration * 1000;

        float npsNoBreak = noteCount / chartDurationNoBreaks * 1000;
        float npsNoBreakNoChain = noteWithoutChainCount / chartDurationNoBreaks * 1000;

        AddMessage($"—————— Notes Per Second ——————————\n" +
                   $"NPS with Chains              : {nps:0.0000}\n" +
                   $"NPS without Chains           : {npsNoChain:0.0000}\n\n" +
                   $"NPS without Breaks           : {npsNoBreak:0.0000}\n" +
                   $"NPS without Chains or Breaks : {npsNoBreakNoChain:0.0000}\n\n");
    }

    private void GetHeatmapValues()
    {
        heatmapWeights = new float[60];
        if (chart is null) return;

        const float noteWeight = 1.0f;
        const float holdWeight = 0.2f;

        // add weights for notes
        foreach (Note note in chart.objects)
        {
            if (note.NoteType is Enums.NoteType.EndChart) continue;

            for (int i = note.Position; i < note.Position + note.Size; i++)
            {
                int modulo = (i % 60 + 60) % 60;
                heatmapWeights[modulo] += note.IsHold ? holdWeight : noteWeight;
            }
        }

        float max = heatmapWeights.Max();
        if (max == 0) return;

        foreach (ref float weight in heatmapWeights.AsSpan())
        {
            weight /= max;
        }
    }

    private void GetSkillRadarValues()
    {
        skillRadarValues = [0, 0, 0];

        // Stamina/Speed Rating:
        int noteCount = chart.nonSegmentNotes.Count;
        float chartDuration = chart.nonSegmentNotes.Last().Time;
        float chartDurationNoBreaks = chartDuration;

        const float pauseThreshold = 2500; // ms

        for (int i = 0; i < noteCount - 1; i++)
        {
            var note = chart.nonSegmentNotes[i];
            var next = chart.nonSegmentNotes[i + 1];

            if (next.Time - note.Time > pauseThreshold)
                chartDurationNoBreaks -= next.Time - note.Time;
        }

        chartDuration = float.Max(0.001f, chartDuration);
        chartDurationNoBreaks = float.Max(0.001f, chartDurationNoBreaks);

        float nps = float.Max(0.001f, noteCount / chartDuration * 1000);
        float npsNoBreaks = float.Max(0.001f, noteCount / chartDurationNoBreaks * 1000);

        // 12nps is fucking nuts, that's gonna be the max value.
        // Möbius has 10.0 min / 10.8 max for reference.
        float speedRating = float.Clamp(npsNoBreaks / 12, 0, 1);

        float staminaRating = (MathF.Pow(nps / npsNoBreaks, 4) * 0.5f) + (speedRating * 0.5f);

        float complexityRating = 0;

        for (int i = 1; i < noteCount; i++)
        {
            var last = chart.nonSegmentNotes[i - 1];
            var note = chart.nonSegmentNotes[i];

            float complexity = 0;

            if (note.NoteType != last.NoteType && note.NoteType is not Enums.NoteType.Touch)
                complexity += 1.0f;

            if (note.IsSnap || note.IsSwipe)
                complexity += 1.25f;

            if (note.Measure == last.Measure && note.Tick == last.Tick)
                complexity += 2.0f;

            complexityRating += complexity;
        }

        complexityRating /= (noteCount * 3);

        skillRadarValues[0] = speedRating;
        skillRadarValues[1] = staminaRating;
        skillRadarValues[2] = complexityRating;
    }

    private void GetHighEBpm()
    {
        AddMessage($"—————— High eBPM ——————————\n");

        for (int i = 1; i < chart.nonSegmentNotes.Count; i++)
        {
            var current = chart.nonSegmentNotes[i];
            var prev = chart.nonSegmentNotes[i - 1];

            if (current.NoteType is Enums.NoteType.Chain) continue;
            if (current.NoteType is not Enums.NoteType.Touch or Enums.NoteType.HoldStart && prev.NoteType is Enums.NoteType.Chain) continue;
            if (current.Measure == prev.Measure && current.Tick == prev.Tick) continue;

            // intervals for jacks
            const float minBadInterval =  83; // sliding becomes possible
            const float maxBadInterval = 120; // streams get too uncomfortable

            float interval = current.Time - prev.Time;
            if (current.Parity != prev.Parity) interval *= 2;

            if (interval is < maxBadInterval and > minBadInterval)
                AddMessage($"{current.NoteType} with high eBPM @ {current.Measure} {current.Tick}\n", MessageType.Suggestion);
        }

        AddMessage("\n");
    }

    private void GetVisionBlocks()
    {
        AddMessage($"—————— Vision Blocks ——————————\n");

        List<Note> checkedNotes = [];
        const float visionTimeStart = 500; // ms
        const float visionReactionTime = 200; // ms
        const float blockedPositionRange = 15;
        const float hiSpeedVisionLimit = 4;

        for (int i = 0; i < chart.notes.Count; i++)
        {
            var current = chart.notes[i];
            if (current.CenterPoint > 30) continue;
            if (current.CenterPoint > 15 && current.Parity == Enums.Parity.Left) continue;
            if (current.CenterPoint < 15 && current.Parity == Enums.Parity.Right) continue;

            var possibleVisionBlocks = chart.notes.Where(x => x.Time > current.Time + visionTimeStart && x.Time < current.Time + visionTimeStart + visionReactionTime);

            foreach (Note note in possibleVisionBlocks)
            {
                if (checkedNotes.Contains(note)) continue;
                checkedNotes.Add(note);

                if (note.NoteType is Enums.NoteType.HoldSegment or Enums.NoteType.HoldEnd) continue;

                float loopedCenterPoint = note.CenterPoint > 30 ? note.CenterPoint - 60 : note.CenterPoint;
                if (current.Parity == Enums.Parity.Left && !(note.CenterPoint > current.CenterPoint && note.CenterPoint < current.CenterPoint + blockedPositionRange)) continue;
                if (current.Parity == Enums.Parity.Right && !(loopedCenterPoint < current.CenterPoint && loopedCenterPoint > current.CenterPoint - blockedPositionRange)) continue;

                AddMessage($"{note.NoteType} @ {note.Measure} {note.Tick} vision blocked by {current.NoteType} @ {current.Measure} {current.Tick}\n", MessageType.Suggestion);
            }
        }

        for (int i = 0; i < chart.hiSpeedGimmicks.Count; i++)
        {
            var gimmick = chart.hiSpeedGimmicks[i];
            if (gimmick.HiSpeed < hiSpeedVisionLimit) continue;

            float nextGimmickTime = i < chart.hiSpeedGimmicks.Count - 1 ? chart.hiSpeedGimmicks[i + 1].Time : float.PositiveInfinity;

            var blockedNotes = chart.nonSegmentNotes.Where(x => x.Time > gimmick.Time + visionReactionTime && (x.Time <= nextGimmickTime || x.Time <= gimmick.Time + visionReactionTime * 2));
            if (!blockedNotes.Any()) continue;

            AddMessage($"HiSpeed Change [{gimmick.HiSpeed}] @ {gimmick.Measure} {gimmick.Tick} may cause vision or reaction time issues. Affected notes are:\n", MessageType.Warning);

            foreach (var note in blockedNotes)
            {
                AddMessage($"   {note.NoteType} @ {note.Measure} {note.Tick}\n");
            }

            if (i != chart.hiSpeedGimmicks.Count - 1)
                AddMessage("\n");
        }

        AddMessage("\n");
    }

    private void GetLevel()
    {
        GetSkillRadarValues();

        const float speedWeight = 1.5f;
        const float staminaWeight = 0.6f;
        const float complexityWeight = 1.0f;
        const float levelMultiplier = 5.95f;
        const float power = 2.0f;
        const float adjust = 0.2f;

        float speed = OutEase(skillRadarValues[0], power);
        float stamina = OutEase(skillRadarValues[1], power);
        float complexity = OutEase(skillRadarValues[2], power);

        speed *= AdjustLevel(speed, adjust);
        stamina *= AdjustLevel(stamina, adjust);
        complexity *= AdjustLevel(complexity, adjust);

        float level = (speed * speedWeight + stamina * staminaWeight + complexity * complexityWeight) * levelMultiplier;

        AddMessage($"—————— Level Estimate ——————————\n" +
                   $"{level:0.000}\n\n");
    }

    private static float OutEase(float x, float pow)
    {
        return 1 - MathF.Pow(1 - x, pow);
    }

    private static float AdjustLevel(float x, float y)
    {
        return 1 - y * (1 - x) * (1 - x);
    }

    private void GetParity()
    {
        AddMessage($"—————— Parity ——————————\n");

        foreach (Note note in chart.notes)
        {
            if (note.NoteType is Enums.NoteType.HoldSegment or Enums.NoteType.HoldEnd) continue;
            AddMessage($"{note.NoteType} {note.Measure} {note.Tick} — {note.Parity}\n", MessageType.Debug);
        }

        AddMessage("\n");
    }

}