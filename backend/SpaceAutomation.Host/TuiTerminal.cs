using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Data;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace SpaceAutomation.Host;

// Terminal.Gui v2 frontend: buttons replace the :commands, tabs hold the
// console log and a live world table, and a status line shows session state.
// The UI thread never touches the world; it drains the session's output queue
// and polls published state on a 100 ms timer.
public static class TuiTerminal
{
    public static void Run(GameSession session, ConcurrentQueue<string> output)
    {
        using var app = Application.Create();
        app.Init();

        var window = new Window { Title = "Space Automation" };
        var logLines = new ObservableCollection<string> { "controls: Pause/Step/Restart buttons · input line evaluates Lua · :commands still work" };
        var objectsSignature = "";

        var log = new ListView { Title = "Console", X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(), Source = new ListWrapper<string>(logLines) };
        var table = new TableView { Title = "Objects", X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(), FullRowSelect = true };
        var dataTable = new DataTable();
        dataTable.Columns.Add("Id", typeof(string));
        dataTable.Columns.Add("Type", typeof(string));
        dataTable.Columns.Add("Position", typeof(string));
        dataTable.Columns.Add("Detail", typeof(string));
        table.Table = new DataTableSource(dataTable);

        // Tabs renders every subview as one tab; the subview's Title is the header.
        var tabs = new Tabs { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(3) };
        tabs.Add(log);
        tabs.Add(table);

        var history = new List<string>();
        var historyIndex = 0;
        var input = new TextField { X = 0, Y = Pos.AnchorEnd(2), Width = Dim.Fill() };
        input.Accepting += (_, e) =>
        {
            var line = input.Text?.ToString() ?? "";
            if (line.Length > 0)
            {
                session.EvaluateLine(line);
                history.Add(line);
                historyIndex = history.Count;
                input.Text = "";
            }
            e.Handled = true;
        };
        input.KeyDown += (_, key) =>
        {
            if (key == Key.CursorUp && historyIndex > 0) { historyIndex--; input.Text = history[historyIndex]; key.Handled = true; }
            else if (key == Key.CursorDown && historyIndex < history.Count)
            {
                historyIndex++;
                input.Text = historyIndex < history.Count ? history[historyIndex] : "";
                key.Handled = true;
            }
        };

        var pause = new Button { Text = "Pause", X = 0, Y = Pos.AnchorEnd(1) };
        var step = new Button { Text = "Step", X = Pos.Right(pause) + 1, Y = Pos.AnchorEnd(1) };
        var restart = new Button { Text = "Restart", X = Pos.Right(step) + 1, Y = Pos.AnchorEnd(1) };
        var quit = new Button { Text = "Quit", X = Pos.Right(restart) + 1, Y = Pos.AnchorEnd(1) };
        var status = new Label { X = Pos.Right(quit) + 2, Y = Pos.AnchorEnd(1), Width = Dim.Fill() };
        pause.Accepted += (_, _) => session.PauseOrResume();
        step.Accepted += (_, _) => session.Step();
        restart.Accepted += (_, _) => session.Restart();
        quit.Accepted += (_, _) => { session.Quit(); app.RequestStop(); };
        window.KeyDown += (_, key) => { if (key == Key.Q.WithCtrl) { session.Quit(); app.RequestStop(); } };

        window.Add(tabs, input, pause, step, restart, quit, status);
        input.SetFocus();

        app.AddTimeout(TimeSpan.FromMilliseconds(100), () =>
        {
            var appended = false;
            while (output.TryDequeue(out var line))
            {
                logLines.Add(line);
                if (logLines.Count > 2000) logLines.RemoveAt(0);
                appended = true;
            }
            if (appended) log.MoveEnd(false);
            pause.Text = session.Paused ? "Resume" : "Pause";
            status.Text = $"tick {session.World.Tick} · {(session.Paused ? "paused" : "running")}"
                + (session.Latched ? " · script failed — Restart" : "") + $" · {session.World.Objects.Count} objects";
            var signature = string.Join("|", session.Objects.Select(row => $"{row.Id};{row.Position};{row.Detail}"));
            if (signature != objectsSignature)
            {
                objectsSignature = signature;
                dataTable.Rows.Clear();
                foreach (var row in session.Objects) dataTable.Rows.Add(row.Id, row.Type, row.Position, row.Detail);
                table.Table = new DataTableSource(dataTable);
            }
            return true;
        });

        app.Run(window);
        session.Dispose();
    }
}
