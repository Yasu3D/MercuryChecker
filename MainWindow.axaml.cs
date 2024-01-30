using Avalonia.Controls;
using Avalonia.Input;
using System.Linq;
using System.IO;
using MercuryMapChecker.ChartData;
using System;
using SkiaSharp;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Threading;

namespace MercuryMapChecker
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            AddHandler(DragDrop.DropEvent, MainWindow_Drop);
        }

        private SkiaRenderEngine renderEngine = new();
        private Chart chart = new();

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
            Tip = 1,
            Warning = 2,
            Error = 3
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

            chart.ParseFile(path);
            filename = Path.GetFileName(Uri.UnescapeDataString(path.LocalPath));

            RemoveMessages();
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
                case MessageType.Tip:
                    Dispatcher.UIThread.Invoke(() => TestResults.Inlines.Add(new Run("TIP:  ") { FontWeight = FontWeight.Bold, Foreground = Brushes.Turquoise }));
                    break;

                case MessageType.Warning:
                    Dispatcher.UIThread.Invoke(() => TestResults.Inlines.Add(new Run("WARNING:  ") { FontWeight = FontWeight.Bold, Foreground = Brushes.Orange }));
                    break;

                case MessageType.Error:
                    Dispatcher.UIThread.Invoke(() => TestResults.Inlines.Add(new Run("ERROR:  ") { FontWeight = FontWeight.Bold, Foreground = Brushes.Red }));
                    break;
            }

            Dispatcher.UIThread.Invoke(() => TestResults.Inlines.Add(message));
        }

        private void RemoveMessages()
        {
            TestResults.Inlines?.Clear();
            TestResults.Inlines?.Add(new Run("")); // I hate this but it makes it work
        }

        /// <summary>
        /// Updates the results text and any other UI stuff when a new chart is loaded.
        /// </summary>
        private void WriteResults()
        {
            if (TestResults.Inlines == null) return;
            if (!chart.loaded)
            {
                TestResults.Inlines.Add("Invalid file!");
                HeatmapCanvas.IsVisible = false;
                SkillRadarCanvas.IsVisible = false;
                return;
            }

            AddMessage($"{filename}\n\n", MessageType.None);

            if (ShowNotesInvalid.IsChecked != null && (bool)ShowNotesInvalid.IsChecked)
                GetNotesInvalid();

            //if (ShowNotesOverlap.IsChecked != null && (bool)ShowNotesOverlap.IsChecked)

            if (ShowNotesSmall.IsChecked != null && (bool)ShowNotesSmall.IsChecked)
                GetNotesSmall();

            if (ShowHoldsInvalid.IsChecked != null && (bool)ShowHoldsInvalid.IsChecked)
                GetHoldsInvalid();

            //if (ShowHoldsUnbaked.IsChecked != null && (bool)ShowHoldsUnbaked.IsChecked)

            //if (ShowHoldsTooFast.IsChecked != null && (bool)ShowHoldsTooFast.IsChecked)

            //if (ShowTimingOdd.IsChecked != null && (bool)ShowTimingOdd.IsChecked)

            //if (ShowTimingTooFast.IsChecked != null && (bool)ShowTimingTooFast.IsChecked)

            //if (ShowPlayabilityVision.IsChecked != null && (bool)ShowPlayabilityVision.IsChecked)

            //if (ShowPlayabilityMovement.IsChecked != null && (bool)ShowPlayabilityMovement.IsChecked)

            //if (ShowPlayabilityDirectionBias.IsChecked != null && (bool)ShowPlayabilityDirectionBias.IsChecked)

            if (ShowStatsCounts.IsChecked != null && (bool)ShowStatsCounts.IsChecked)
                GetNoteCount();

            //if (ShowStatsNPS.IsChecked != null && (bool)ShowStatsNPS.IsChecked)

            //if (ShowStatsLevel.IsChecked != null && (bool)ShowStatsLevel.IsChecked)

            if (ShowStatsHeatmap.IsChecked != null && (bool)ShowStatsHeatmap.IsChecked)
                GetHeatmapValues();

            if (ShowStatsSkill.IsChecked != null && (bool)ShowStatsSkill.IsChecked)
                GetSkillRadarValues();

            HeatmapGroup.IsVisible = ShowStatsHeatmap.IsChecked ?? false;
            SkillTriangleGroup.IsVisible = ShowStatsSkill.IsChecked ?? false;
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            e.Handled = true;
        }

        private void GetNotesInvalid()
        {
            AddMessage($"—————— Invalid Notes ——————————\n");

            foreach (Note note in chart.notes)
            {
                if (note.Size is < 1 or > 60)
                    AddMessage($"{note.NoteType} with invalid size ({note.Size}) @ {note.Measure} {note.Tick}\n", MessageType.Error);

                if (note.Position is < 0 or > 59)
                    AddMessage($"{note.NoteType} with invalid position ({note.Position}) @ {note.Measure} {note.Tick}\n", MessageType.Error);

                if (note.NoteType is ObjectEnums.NoteType.None)
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
                    AddMessage($"{note.NoteType} with small size ({note.Size}) @ {note.Measure} {note.Tick}\n", MessageType.Warning);
            }

            AddMessage("\n");
        }

        private void GetHoldsInvalid()
        {
            AddMessage($"—————— Invalid Holds ——————————\n");

            foreach (Note note in chart.notes)
            {
                if (note.NoteType is ObjectEnums.NoteType.HoldStart or ObjectEnums.NoteType.HoldSegment && note.NextReferencedNote is null)
                {
                    AddMessage($"{note.NoteType} without reference @ {note.Measure} {note.Tick}\n", MessageType.Error);
                }
            }

            AddMessage("\n");
        }

        private void GetNoteCount()
        {
            if (chart is null) return;

            var tapNotes = chart.notes.Where(x => x.NoteType is ObjectEnums.NoteType.Touch).Count();
            var chainNotes = chart.notes.Where(x => x.NoteType is ObjectEnums.NoteType.Chain).Count();
            var holdNotes = chart.notes.Where(x => x.NoteType is ObjectEnums.NoteType.HoldStart).Count();
            var cwSwipeNotes = chart.notes.Where(x => x.NoteType is ObjectEnums.NoteType.SwipeClockwise).Count();
            var ccwSwipeNotes = chart.notes.Where(x => x.NoteType is ObjectEnums.NoteType.SwipeCounterclockwise).Count();
            var fwSnapNotes = chart.notes.Where(x => x.NoteType is ObjectEnums.NoteType.SnapForward).Count();
            var bwSnapNotes = chart.notes.Where(x => x.NoteType is ObjectEnums.NoteType.SnapBackward).Count();

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
                if (note.NoteType is ObjectEnums.NoteType.EndChart) continue;

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

            skillRadarValues[0] = 0.5f;
            skillRadarValues[1] = 0.75f;
            skillRadarValues[2] = 1.0f;
        }

        private void GetLevel() { }
    }
}