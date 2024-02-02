using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using static System.Net.Mime.MediaTypeNames;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace MercuryChecker.ChartData;

public class Chart
{
    private int readerIndex;
    private List<Gimmick> bpmGimmicks = [];
    private List<Gimmick> timeSigGimmicks = [];
    private readonly string[] separator = [" "];
    private const float tickToMeasure = 1.0f / 1920.0f;

    public bool loaded = false;
    public List<Gimmick> bgmDataGimmicks = [];
    public List<Gimmick> hiSpeedGimmicks = [];
    public List<Gimmick> stopGimmicks = [];
    public List<Gimmick> reverseGimmicks = [];
    public List<Note> objects = [];
    public List<Note> masks = [];
    public List<Note> notes = [];
    public List<Note> nonSegmentNotes = [];

    public void ParseFile(Uri uri)
    {
        readerIndex = 0;
        bpmGimmicks.Clear();
        timeSigGimmicks.Clear();
        bgmDataGimmicks.Clear();
        hiSpeedGimmicks.Clear();
        reverseGimmicks.Clear();
        objects.Clear();
        masks.Clear();
        notes.Clear();
        nonSegmentNotes.Clear();

        string path = Uri.UnescapeDataString(uri.LocalPath);
        loaded = false;
        if (!File.Exists(path) || Path.GetExtension(path) != ".mer") return;

        FileStream fileStream = new(path, FileMode.Open, FileAccess.Read);
        List<string> merFile = LoadMer(fileStream);

        if (merFile == null) return;

        // ======== Skip Metadata
        do
        {
            string merLine = merFile[readerIndex];

            if (merLine.Contains("#BODY"))
            {
                readerIndex++;
                break;
            }
        }
        while (++readerIndex < merFile.Count);

        // ======== Parse File
        Note tempNote;
        Gimmick tempGimmick;

        Dictionary<int, Note> notesByLine = new();
        Dictionary<int, int> refByLine = new();

        for (int i = readerIndex; i < merFile.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(merFile[i])) continue;
            string[] parsed = merFile[i].Split(separator, StringSplitOptions.RemoveEmptyEntries);

            int measure = Convert.ToInt32(parsed[0]);
            int tick = Convert.ToInt32(parsed[1]);
            int objectId = Convert.ToInt32(parsed[2]);

            // Invalid ID
            if (objectId == 0) continue;

            // Note
            if (objectId == 1)
            {
                int noteTypeID = Convert.ToInt32(parsed[3]);
                int noteIndex = Convert.ToInt32(parsed[4]);
                int position = Convert.ToInt32(parsed[5]);
                int size = Convert.ToInt32(parsed[6]);

                tempNote = new Note(measure, tick, noteTypeID, position, size);

                // hold start & segments
                if ((tempNote.NoteType == Enums.NoteType.HoldStart || tempNote.NoteType == Enums.NoteType.HoldSegment) && parsed.Length >= 9)
                {
                    refByLine[noteIndex] = Convert.ToInt32(parsed[8]);
                }

                // mask notes
                if (noteTypeID is 12 or 13)
                {
                    int dir = Convert.ToInt32(parsed[8]);
                    tempNote.MaskDirection = (Enums.MaskDirection)dir;
                    masks.Add(tempNote);
                }
                else
                {
                    notes.Add(tempNote);

                    if (noteTypeID is not (10 or 11))
                        nonSegmentNotes.Add(tempNote);
                }

                objects.Add(tempNote);
                notesByLine[noteIndex] = tempNote;
            }

            // Gimmick
            else
            {
                // create a gimmick
                object? value1 = null;
                object? value2 = null;

                // avoid IndexOutOfRangeExceptions :]
                if (objectId is 3 && parsed.Length > 4)
                {
                    value1 = Convert.ToInt32(parsed[3]);
                    value2 = Convert.ToInt32(parsed[4]);
                }

                if (objectId is 3 && parsed.Length == 4)
                {
                    // Edge case. some old charts apparently have broken time sigs.
                    value1 = Convert.ToInt32(parsed[3]);
                    value2 = Convert.ToInt32(parsed[3]);
                }

                if (objectId is 2 or 5 && parsed.Length > 3)
                    value1 = Convert.ToSingle(parsed[3]);

                tempGimmick = new Gimmick(measure, tick, objectId, value1, value2);

                // sort gimmicks by type
                switch (tempGimmick.GimmickType)
                {
                    case Enums.GimmickType.BeatsPerMinute:
                        bpmGimmicks.Add(tempGimmick);
                        break;
                    case Enums.GimmickType.TimeSignature:
                        timeSigGimmicks.Add(tempGimmick);
                        break;
                    case Enums.GimmickType.HiSpeed:
                        hiSpeedGimmicks.Add(tempGimmick);
                        break;
                    case Enums.GimmickType.StopStart:
                        tempGimmick.GimmickType = Enums.GimmickType.StopStart;
                        stopGimmicks.Add(tempGimmick);
                        break;
                    case Enums.GimmickType.StopEnd:
                        tempGimmick.GimmickType = Enums.GimmickType.StopEnd;
                        stopGimmicks.Add(tempGimmick);
                        break;
                    case Enums.GimmickType.ReverseEffectStart:
                    case Enums.GimmickType.ReverseEffectEnd:
                    case Enums.GimmickType.ReverseNoteEnd:
                        reverseGimmicks.Add(tempGimmick);
                        break;
                }
            }
        }

        for (int i = 0; i < objects.Count; i++)
        {
            if (refByLine.ContainsKey(i))
            {
                if (!notesByLine.ContainsKey(refByLine[i]))
                {
                    continue;
                }

                notesByLine[i].NextReferencedNote = notesByLine[refByLine[i]];
                notesByLine[i].NextReferencedNote.PrevReferencedNote = notesByLine[i];
            }

        }

        // ======== Generate BGMData
        if (bpmGimmicks.Count == 0 || timeSigGimmicks.Count == 0) return;

        float lastBpm = bpmGimmicks[0].BeatsPerMinute;
        TimeSignature? lastTimeSig = timeSigGimmicks[0].TimeSig;

        bgmDataGimmicks = [.. bpmGimmicks.Concat(timeSigGimmicks).OrderBy(x => x.Measure * 1920 + x.Tick)];

        bgmDataGimmicks[0].BeatsPerMinute = lastBpm;
        bgmDataGimmicks[0].TimeSig = lastTimeSig;

        int lastTick = 0;

        List<Gimmick> obsoleteGimmicks = [];

        for (int i = 1; i < bgmDataGimmicks.Count; i++)
        {
            int currentTick = bgmDataGimmicks[i].Measure * 1920 + bgmDataGimmicks[i].Tick;

            // Handles two gimmicks at the same time, in case a chart changes
            // BeatsPerMinute and TimeSignature simultaneously.
            if (currentTick == lastTick)
            {
                // if this is a bpm change, then last change must've been a time sig change.
                if (bgmDataGimmicks[i].GimmickType is Enums.GimmickType.BeatsPerMinute)
                {
                    bgmDataGimmicks[i - 1].BeatsPerMinute = bgmDataGimmicks[i].BeatsPerMinute;
                    lastBpm = bgmDataGimmicks[i].BeatsPerMinute;
                }
                if (bgmDataGimmicks[i].GimmickType is Enums.GimmickType.TimeSignature)
                {
                    bgmDataGimmicks[i - 1].TimeSig = bgmDataGimmicks[i].TimeSig;
                    lastTimeSig = bgmDataGimmicks[i].TimeSig;
                }

                // send gimmick to list for removal later
                obsoleteGimmicks.Add(bgmDataGimmicks[i]);
                continue;
            }

            if (bgmDataGimmicks[i].GimmickType is Enums.GimmickType.BeatsPerMinute)
            {
                bgmDataGimmicks[i].TimeSig = lastTimeSig;
                lastBpm = bgmDataGimmicks[i].BeatsPerMinute;
            }

            if (bgmDataGimmicks[i].GimmickType is Enums.GimmickType.TimeSignature)
            {
                bgmDataGimmicks[i].BeatsPerMinute = lastBpm;
                lastTimeSig = bgmDataGimmicks[i].TimeSig;
            }

            lastTick = currentTick;
        }

        // clear obsolete gimmicks
        foreach (Gimmick gimmick in obsoleteGimmicks)
            bgmDataGimmicks.Remove(gimmick);

        obsoleteGimmicks.Clear();

        bgmDataGimmicks[0].Time = 0;
        for (int i = 1; i < bgmDataGimmicks.Count; i++)
        {
            float lastTime = bgmDataGimmicks[i - 1].Time;
            float currentMeasure = (bgmDataGimmicks[i].Measure * 1920 + bgmDataGimmicks[i].Tick) * tickToMeasure;
            float lastMeasure = (bgmDataGimmicks[i - 1].Measure * 1920 + bgmDataGimmicks[i - 1].Tick) * tickToMeasure;
            float timeSig = bgmDataGimmicks[i - 1].TimeSig.Ratio;
            float bpm = bgmDataGimmicks[i - 1].BeatsPerMinute;

            float time = lastTime + ((currentMeasure - lastMeasure) * (4 * timeSig * (60000f / bpm)));
            bgmDataGimmicks[i].Time = time;
        }

        foreach (Note note in notes)
            note.Time = GetTime(note);

        foreach (Gimmick gim in hiSpeedGimmicks)
            gim.Time = GetTime(gim);

        foreach (Gimmick gim in stopGimmicks)
            gim.Time = GetTime(gim);

        foreach (Gimmick gim in reverseGimmicks)
            gim.Time = GetTime(gim);

        InterpretChart();

        loaded = true;
    }

    /// <summary>
    /// A (hopefully) basic algorithm to interpret a chart's parity.
    /// </summary>
    public void InterpretChart()
    {
        foreach (Note note in notes)
        {
            note.CenterPoint = Utils.Modulo(note.Position + (note.Size * 0.5f), 60);
        }

        for (int i = 0; i < notes.Count; i++)
        {
            var current = notes[i];
            var previous = i > 0 ? notes[i - 1] : null;
            var next = i < notes.Count - 1 ? notes[i + 1] : null;

            current.Parity = GetParityContext(current, previous, next);
        }
    }

    public List<string> LoadMer(Stream stream)
    {
        List<string> lines = [];
        StreamReader reader = new(stream);
        while (!reader.EndOfStream)
            lines.Add(reader.ReadLine() ?? "");
        return lines;
    }

    public float GetTime(ChartObject chartObject)
    {
        int timeStamp = chartObject.Measure * 1920 + chartObject.Tick;
        Gimmick lastBgmData = bgmDataGimmicks.LastOrDefault(x => x.Measure * 1920 + x.Tick < timeStamp) ?? bgmDataGimmicks[0];

        float lastTime = lastBgmData.Time;
        float currentMeasure = (chartObject.Measure * 1920 + chartObject.Tick) * tickToMeasure;
        float lastMeasure = (lastBgmData.Measure * 1920 + lastBgmData.Tick) * tickToMeasure;
        float timeSig = lastBgmData.TimeSig.Ratio;
        float bpm = lastBgmData.BeatsPerMinute;

        float time = lastTime + ((currentMeasure - lastMeasure) * (4 * timeSig * (60000f / bpm)));
        return time;
    }

    public static Enums.Parity GetParityContext(Note note, Note prev, Note next)
    {
        const float directionConstant = 1f;
        const float prevParityConstant = 0.25f;
        const float nextDirectionConstant = 1.5f;

        // [0] - special edge cases
        if (prev != null) 
        {
            if (prev.NoteType is Enums.NoteType.HoldStart or Enums.NoteType.HoldSegment && note.PrevReferencedNote != null)
            {
                return note.PrevReferencedNote.Parity;
            }

            // parity stays the same if note position does not change much.
            float posDifference = float.Abs(note.CenterPoint - prev.CenterPoint);
            if (posDifference > 30) posDifference = 60 - posDifference;
            if (posDifference < note.Size * 0.5f) return prev.Parity;

            // preserve parity on chain slides
            if (posDifference < note.Size && prev.NoteType is Enums.NoteType.Chain && note.NoteType is Enums.NoteType.Chain) return prev.Parity;
        }

        // [1] - parity must be from note position.
        float directionWeight = MathF.Cos(MathF.PI * note.CenterPoint * 0.0333333333f);

        // [2] - parity must be opposite of previous note.
        float prevParityWeight;
        if (prev is null) prevParityWeight = 0;
        else prevParityWeight = -(int)prev.Parity;

        // [3] - parity must be relative to next note's position.
        float nextDirection;
        if (next is null) nextDirection = directionWeight;
        else nextDirection = MathF.Cos(MathF.PI * next.CenterPoint * 0.0333333333f);

        float nextDirectionWeight = (directionWeight - nextDirection) * 0.5f;

        // [4] - combine weights and evaluate.
        var parity = (directionWeight * directionConstant + prevParityWeight * prevParityConstant + nextDirectionWeight * nextDirectionConstant) switch
        {
            < -0.05f => Enums.Parity.Left,
            > 0.05f => Enums.Parity.Right,
            _ => Enums.Parity.Ambiguous
        };

        return parity;
    }
}

public class ChartObject(int measure, int tick)
{
    public int Measure = measure;
    public int Tick = tick;
    public float Time;
}

public class TimeSignature(int upper, int lower)
{
    public int Upper = upper;
    public int Lower = lower;
    public float Ratio = (float)upper / (float)lower;

    public static TimeSignature Default { get; private set; } = new(4, 4);
}

public class Note : ChartObject
{
    public Note(Note note) : base(note.Measure, note.Tick)
    {
        Measure = note.Measure;
        Tick = note.Tick;
        Time = note.Time;
        Position = note.Position;
        Size = note.Size;
        NoteType = note.NoteType;
        BonusType = note.BonusType;
        RenderFlag = note.RenderFlag;
        MaskDirection = note.MaskDirection;
        NextReferencedNote = note.NextReferencedNote;
        PrevReferencedNote = note.PrevReferencedNote;
        CenterPoint = note.CenterPoint;
        Parity = note.Parity;
    }

    public Note(int measure, int tick, int noteID, int position, int size, bool renderFlag = true, Note? nextReferencedNote = null, Note? prevReferencedNote = null, Enums.MaskDirection maskDirection = Enums.MaskDirection.None) : base(measure, tick)
    {
        Measure = measure;
        Tick = tick;
        Position = position;
        Size = size;
        RenderFlag = renderFlag;
        MaskDirection = maskDirection;
        NextReferencedNote = nextReferencedNote;
        PrevReferencedNote = prevReferencedNote;

        // assign noteType
        NoteType = noteID switch
        {
            1 or 2 or 20 => Enums.NoteType.Touch,
            3 or 21 => Enums.NoteType.SnapForward,
            4 or 22 => Enums.NoteType.SnapBackward,
            5 or 6 or 23 => Enums.NoteType.SwipeClockwise,
            7 or 8 or 24 => Enums.NoteType.SwipeCounterclockwise,
            9 or 25 => Enums.NoteType.HoldStart,
            10 => Enums.NoteType.HoldSegment,
            11 => Enums.NoteType.HoldEnd,
            12 => Enums.NoteType.MaskAdd,
            13 => Enums.NoteType.MaskRemove,
            14 => Enums.NoteType.EndChart,
            16 or 26 => Enums.NoteType.Chain,
            _ => Enums.NoteType.None,
        };

        // assign bonusType
        BonusType = noteID switch
        {
            1 or 3 or 4 or 5 or 7 or 9 or 10 or 11 or 12 or 13 or 14 or 16 => Enums.BonusType.None,
            2 or 6 or 8 => Enums.BonusType.Bonus,
            20 or 21 or 22 or 23 or 24 or 25 or 26 => Enums.BonusType.R_Note,
            _ => Enums.BonusType.None,
        };
    }

    public int Position;
    public int Size;
    public Enums.NoteType NoteType;
    public Enums.BonusType BonusType;
    public Enums.MaskDirection MaskDirection;
    public bool RenderFlag;

    public bool IsHold => NoteType is Enums.NoteType.HoldStart or Enums.NoteType.HoldSegment or Enums.NoteType.HoldEnd;
    public bool IsSwipe => NoteType is Enums.NoteType.SwipeClockwise or Enums.NoteType.SwipeCounterclockwise;
    public bool IsSnap => NoteType is Enums.NoteType.SnapForward or Enums.NoteType.SnapBackward;

    public Note? NextReferencedNote { get; set; }
    public Note? PrevReferencedNote { get; set; }

    public float CenterPoint = 0;
    public Enums.Parity Parity;
}

public class Gimmick : ChartObject
{
    public Gimmick(int measure, int tick, Enums.GimmickType gimmickType, object? value1 = null, object? value2 = null) : base(measure, tick)
    {
        Measure = measure;
        Tick = tick;
        GimmickType = gimmickType;

        switch (GimmickType)
        {
            default:
                break;

            case Enums.GimmickType.BeatsPerMinute:
                BeatsPerMinute = Convert.ToSingle(value1);
                break;

            case Enums.GimmickType.TimeSignature:
                TimeSig = new TimeSignature(Convert.ToInt32(value1), Convert.ToInt32(value2));
                break;

            case Enums.GimmickType.HiSpeed:
                HiSpeed = Convert.ToSingle(value1);
                break;
        }
    }

    public Gimmick(int measure, int tick, int gimmickID, object? value1 = null, object? value2 = null) : base(measure, tick)
    {
        Measure = measure;
        Tick = tick;

        // assign gimmickType and values
        switch (gimmickID)
        {
            case 2:
                GimmickType = Enums.GimmickType.BeatsPerMinute;
                BeatsPerMinute = Convert.ToSingle(value1);
                break;

            case 3:
                GimmickType = Enums.GimmickType.TimeSignature;
                TimeSig = new TimeSignature(Convert.ToInt32(value1), Convert.ToInt32(value2));
                break;

            case 5:
                GimmickType = Enums.GimmickType.HiSpeed;
                HiSpeed = Convert.ToSingle(value1);
                break;

            case 6:
                GimmickType = Enums.GimmickType.ReverseEffectStart;
                break;

            case 7:
                GimmickType = Enums.GimmickType.ReverseEffectEnd;
                break;

            case 8:
                GimmickType = Enums.GimmickType.ReverseNoteEnd;
                break;

            case 9:
                GimmickType = Enums.GimmickType.StopStart;
                break;

            case 10:
                GimmickType = Enums.GimmickType.StopEnd;
                break;

            default:
                GimmickType = Enums.GimmickType.None;
                break;
        }
    }

    public Gimmick(int measure, int tick, float bpm, TimeSignature timeSig) : base(measure, tick)
    {
        Measure = measure;
        Tick = tick;
        BeatsPerMinute = bpm;
        TimeSig = timeSig;
    }

    public Enums.GimmickType GimmickType;
    public float BeatsPerMinute;
    public TimeSignature? TimeSig;
    public float HiSpeed;
}

public class Enums
{
    public enum NoteType
    {
        None,
        Touch,
        SnapForward,
        SnapBackward,
        SwipeClockwise,
        SwipeCounterclockwise,
        HoldStart,
        HoldSegment,
        HoldEnd,
        Chain,
        MaskAdd,
        MaskRemove,
        EndChart
    }
    public enum BonusType
    {
        None,
        Bonus,
        R_Note
    }
    public enum GimmickType
    {
        None,
        Note,
        BeatsPerMinute,
        TimeSignature,
        HiSpeed,
        ReverseEffectStart,
        ReverseEffectEnd,
        ReverseNoteEnd,
        StopStart,
        StopEnd
    }
    public enum MaskDirection
    {
        None = 3,
        Counterclockwise = 0,
        Clockwise = 1,
        Center = 2
    }

    public enum Parity
    {
        Left = -1,
        Ambiguous = 0,
        Right = 1
    }
}
