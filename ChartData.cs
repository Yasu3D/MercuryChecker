using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace MercuryMapChecker.ChartData;

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
                if ((tempNote.NoteType == ObjectEnums.NoteType.HoldStart || tempNote.NoteType == ObjectEnums.NoteType.HoldSegment) && parsed.Length >= 9)
                {
                    refByLine[noteIndex] = Convert.ToInt32(parsed[8]);
                }

                // mask notes
                if (noteTypeID is 12 or 13)
                {
                    int dir = Convert.ToInt32(parsed[8]);
                    tempNote.MaskDirection = (ObjectEnums.MaskDirection)dir;
                    masks.Add(tempNote);
                }
                else
                {
                    notes.Add(tempNote);
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

                if (objectId is 2 or 5 && parsed.Length > 3)
                    value1 = Convert.ToSingle(parsed[3]);

                tempGimmick = new Gimmick(measure, tick, objectId, value1, value2);

                // sort gimmicks by type
                switch (tempGimmick.GimmickType)
                {
                    case ObjectEnums.GimmickType.BeatsPerMinute:
                        bpmGimmicks.Add(tempGimmick);
                        break;
                    case ObjectEnums.GimmickType.TimeSignature:
                        timeSigGimmicks.Add(tempGimmick);
                        break;
                    case ObjectEnums.GimmickType.HiSpeed:
                        hiSpeedGimmicks.Add(tempGimmick);
                        break;
                    case ObjectEnums.GimmickType.StopStart:
                        tempGimmick.GimmickType = ObjectEnums.GimmickType.StopStart;
                        stopGimmicks.Add(tempGimmick);
                        break;
                    case ObjectEnums.GimmickType.StopEnd:
                        tempGimmick.GimmickType = ObjectEnums.GimmickType.StopEnd;
                        stopGimmicks.Add(tempGimmick);
                        break;
                    case ObjectEnums.GimmickType.ReverseEffectStart:
                    case ObjectEnums.GimmickType.ReverseEffectEnd:
                    case ObjectEnums.GimmickType.ReverseNoteEnd:
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
                if (bgmDataGimmicks[i].GimmickType is ObjectEnums.GimmickType.BeatsPerMinute)
                {
                    bgmDataGimmicks[i - 1].BeatsPerMinute = bgmDataGimmicks[i].BeatsPerMinute;
                    lastBpm = bgmDataGimmicks[i].BeatsPerMinute;
                }
                if (bgmDataGimmicks[i].GimmickType is ObjectEnums.GimmickType.TimeSignature)
                {
                    bgmDataGimmicks[i - 1].TimeSig = bgmDataGimmicks[i].TimeSig;
                    lastTimeSig = bgmDataGimmicks[i].TimeSig;
                }

                // send gimmick to list for removal later
                obsoleteGimmicks.Add(bgmDataGimmicks[i]);
                continue;
            }

            if (bgmDataGimmicks[i].GimmickType is ObjectEnums.GimmickType.BeatsPerMinute)
            {
                bgmDataGimmicks[i].TimeSig = lastTimeSig;
                lastBpm = bgmDataGimmicks[i].BeatsPerMinute;
            }

            if (bgmDataGimmicks[i].GimmickType is ObjectEnums.GimmickType.TimeSignature)
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

        loaded = true;
    }

    public List<string> LoadMer(Stream stream)
    {
        List<string> lines = [];
        StreamReader reader = new(stream);
        while (!reader.EndOfStream)
            lines.Add(reader.ReadLine() ?? "");
        return lines;
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
    }

    public Note(int measure, int tick, int noteID, int position, int size, bool renderFlag = true, Note? nextReferencedNote = null, Note? prevReferencedNote = null, ObjectEnums.MaskDirection maskDirection = ObjectEnums.MaskDirection.None) : base(measure, tick)
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
            1 or 2 or 20 => ObjectEnums.NoteType.Touch,
            3 or 21 => ObjectEnums.NoteType.SnapForward,
            4 or 22 => ObjectEnums.NoteType.SnapBackward,
            5 or 6 or 23 => ObjectEnums.NoteType.SwipeClockwise,
            7 or 8 or 24 => ObjectEnums.NoteType.SwipeCounterclockwise,
            9 or 25 => ObjectEnums.NoteType.HoldStart,
            10 => ObjectEnums.NoteType.HoldSegment,
            11 => ObjectEnums.NoteType.HoldEnd,
            12 => ObjectEnums.NoteType.MaskAdd,
            13 => ObjectEnums.NoteType.MaskRemove,
            14 => ObjectEnums.NoteType.EndChart,
            16 or 26 => ObjectEnums.NoteType.Chain,
            _ => ObjectEnums.NoteType.None,
        };

        // assign bonusType
        BonusType = noteID switch
        {
            1 or 3 or 4 or 5 or 7 or 9 or 10 or 11 or 12 or 13 or 14 or 16 => ObjectEnums.BonusType.None,
            2 or 6 or 8 => ObjectEnums.BonusType.Bonus,
            20 or 21 or 22 or 23 or 24 or 25 or 26 => ObjectEnums.BonusType.R_Note,
            _ => ObjectEnums.BonusType.None,
        };
    }

    public int Position;
    public int Size;
    public ObjectEnums.NoteType NoteType;
    public ObjectEnums.BonusType BonusType;
    public ObjectEnums.MaskDirection MaskDirection;
    public bool RenderFlag;

    public bool IsHold => NoteType is ObjectEnums.NoteType.HoldStart or ObjectEnums.NoteType.HoldSegment or ObjectEnums.NoteType.HoldEnd;
    public bool IsSwipe => NoteType is ObjectEnums.NoteType.SwipeClockwise or ObjectEnums.NoteType.SwipeCounterclockwise;
    public bool IsSnap => NoteType is ObjectEnums.NoteType.SnapForward or ObjectEnums.NoteType.SnapBackward;

    public Note? NextReferencedNote { get; set; }
    public Note? PrevReferencedNote { get; set; }
}

public class Gimmick : ChartObject
{
    public Gimmick(int measure, int tick, ObjectEnums.GimmickType gimmickType, object? value1 = null, object? value2 = null) : base(measure, tick)
    {
        Measure = measure;
        Tick = tick;
        GimmickType = gimmickType;

        switch (GimmickType)
        {
            default:
                break;

            case ObjectEnums.GimmickType.BeatsPerMinute:
                BeatsPerMinute = Convert.ToSingle(value1);
                break;

            case ObjectEnums.GimmickType.TimeSignature:
                TimeSig = new TimeSignature(Convert.ToInt32(value1), Convert.ToInt32(value2));
                break;

            case ObjectEnums.GimmickType.HiSpeed:
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
                GimmickType = ObjectEnums.GimmickType.BeatsPerMinute;
                BeatsPerMinute = Convert.ToSingle(value1);
                break;

            case 3:
                GimmickType = ObjectEnums.GimmickType.TimeSignature;
                TimeSig = new TimeSignature(Convert.ToInt32(value1), Convert.ToInt32(value2));
                break;

            case 5:
                GimmickType = ObjectEnums.GimmickType.HiSpeed;
                HiSpeed = Convert.ToSingle(value1);
                break;

            case 6:
                GimmickType = ObjectEnums.GimmickType.ReverseEffectStart;
                break;

            case 7:
                GimmickType = ObjectEnums.GimmickType.ReverseEffectEnd;
                break;

            case 8:
                GimmickType = ObjectEnums.GimmickType.ReverseNoteEnd;
                break;

            case 9:
                GimmickType = ObjectEnums.GimmickType.StopStart;
                break;

            case 10:
                GimmickType = ObjectEnums.GimmickType.StopEnd;
                break;

            default:
                GimmickType = ObjectEnums.GimmickType.None;
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

    public ObjectEnums.GimmickType GimmickType;
    public float BeatsPerMinute;
    public TimeSignature? TimeSig;
    public float HiSpeed;
}

public class ObjectEnums
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
}
