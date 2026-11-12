using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ZeroEngine.World.Editor.WorldGraph
{
    public sealed class WorldGraphWorkbenchWindow : EditorWindow
    {
        private WorldGraphWorkbenchSession _session;
        private ScrollView _scrollView;

        public static WorldGraphWorkbenchWindow Open(
            string title,
            WorldGraphGraduationProfile profile,
            System.Collections.Generic.IEnumerable<WorldGraphWorkbenchAction> actions = null)
        {
            var window = GetWindow<WorldGraphWorkbenchWindow>();
            window.titleContent = new GUIContent(string.IsNullOrWhiteSpace(title) ? "WorldGraph Workbench" : title);
            window.Initialize(new WorldGraphWorkbenchSession(title, profile, actions));
            window.Show();
            return window;
        }

        private void OnEnable()
        {
            BuildRoot();
            Render();
        }

        private void Initialize(WorldGraphWorkbenchSession session)
        {
            _session = session;
            BuildRoot();
            Render();
        }

        private void BuildRoot()
        {
            rootVisualElement.Clear();
            rootVisualElement.style.flexDirection = FlexDirection.Column;
            rootVisualElement.style.paddingLeft = 8;
            rootVisualElement.style.paddingRight = 8;
            rootVisualElement.style.paddingTop = 8;
            rootVisualElement.style.paddingBottom = 8;

            var toolbar = new VisualElement();
            toolbar.style.flexDirection = FlexDirection.Row;
            toolbar.style.marginBottom = 8;
            toolbar.Add(CreateToolbarButton("Run Validation", () => Execute(WorldGraphWorkbenchModel.RunValidationActionId)));
            toolbar.Add(CreateToolbarButton("Run Graduation", () => Execute(WorldGraphWorkbenchModel.RunGraduationActionId)));
            rootVisualElement.Add(toolbar);

            _scrollView = new ScrollView();
            _scrollView.style.flexGrow = 1;
            rootVisualElement.Add(_scrollView);
        }

        private Button CreateToolbarButton(string text, System.Action clicked)
        {
            var button = new Button(clicked) { text = text };
            button.style.marginRight = 6;
            return button;
        }

        private void Render()
        {
            if (_scrollView == null)
            {
                return;
            }

            _scrollView.Clear();
            if (_session == null)
            {
                _scrollView.Add(new Label("No WorldGraph profile is loaded."));
                return;
            }

            var snapshot = _session.Refresh();
            AddGraphSummary(snapshot);
            AddCells(snapshot);
            AddLinks(snapshot);
            AddIssues(snapshot);
            AddActions(snapshot);
            AddHistory(snapshot);
        }

        private void AddGraphSummary(WorldGraphWorkbenchSnapshot snapshot)
        {
            var section = CreateSection("Graph");
            section.Add(new Label("WorldGraph: " + Empty(snapshot.WorldGraphId)));
            section.Add(new Label("Expected: " + Empty(snapshot.ExpectedWorldGraphId)));
            section.Add(new Label("Graph Asset: " + Empty(snapshot.GraphAssetPath)));
            section.Add(new Label("Start: " + Empty(snapshot.StartCellId) + " / " + Empty(snapshot.StartAnchorId)));
            section.Add(new Label("Cells: " + snapshot.Cells.Count + "  Links: " + snapshot.Links.Count + "  Issues: " + snapshot.Issues.Count));
            _scrollView.Add(section);
        }

        private void AddCells(WorldGraphWorkbenchSnapshot snapshot)
        {
            var section = CreateSection("Cells");
            foreach (var cell in snapshot.Cells)
            {
                section.Add(new Label(
                    cell.Status + " | "
                    + cell.CellId + " | "
                    + cell.CellKind + " | "
                    + cell.Layers + " | budget "
                    + cell.BudgetWeight + " | anchors "
                    + cell.AnchorCount + " | boundaries "
                    + cell.StreamingBoundaryCount));
                section.Add(new Label("  Scene: " + Empty(cell.ScenePath) + "  Nav: " + Empty(cell.NavigationAssetPath)));
            }

            if (snapshot.Cells.Count == 0)
            {
                section.Add(new Label("No cells are available."));
            }

            _scrollView.Add(section);
        }

        private void AddLinks(WorldGraphWorkbenchSnapshot snapshot)
        {
            var section = CreateSection("Links");
            foreach (var link in snapshot.Links)
            {
                section.Add(new Label(
                    link.LinkId + " | "
                    + link.TravelMode + " | "
                    + link.FromAnchorId + " -> "
                    + link.ToAnchorId
                    + (link.Bidirectional ? " | bidirectional" : string.Empty)));
            }

            if (snapshot.Links.Count == 0)
            {
                section.Add(new Label("No travel links are available."));
            }

            _scrollView.Add(section);
        }

        private void AddIssues(WorldGraphWorkbenchSnapshot snapshot)
        {
            var section = CreateSection("Validation Issues");
            foreach (var issue in snapshot.Issues)
            {
                section.Add(new Label(
                    issue.Severity + " | "
                    + issue.Code + " | "
                    + issue.Message + " | "
                    + Empty(issue.AssetPath) + " | "
                    + Empty(issue.ContextId)));
            }

            if (snapshot.Issues.Count == 0)
            {
                section.Add(new Label("No validation issues in the current snapshot."));
            }

            _scrollView.Add(section);
        }

        private void AddActions(WorldGraphWorkbenchSnapshot snapshot)
        {
            var section = CreateSection("Actions");
            foreach (var action in snapshot.Actions)
            {
                var button = new Button(() => Execute(action.ActionId)) { text = action.Label };
                button.tooltip = action.Description;
                section.Add(button);
                if (!string.IsNullOrWhiteSpace(action.TargetPath))
                {
                    section.Add(new Label("  " + action.Kind + " | " + action.TargetPath));
                }
            }

            _scrollView.Add(section);
        }

        private void AddHistory(WorldGraphWorkbenchSnapshot snapshot)
        {
            var section = CreateSection("Run History");
            foreach (var record in snapshot.History)
            {
                section.Add(new Label(
                    record.CompletedAtUtc.ToString("u")
                    + " | "
                    + record.Mode
                    + " | issues "
                    + record.IssueCount
                    + " | blocking "
                    + record.BlockingIssueCount
                    + " | "
                    + (record.Passed ? "passed" : "blocked")));
            }

            if (snapshot.History.Count == 0)
            {
                section.Add(new Label("No runs recorded in this session."));
            }

            _scrollView.Add(section);
        }

        private VisualElement CreateSection(string title)
        {
            var section = new VisualElement();
            section.style.marginBottom = 10;
            section.style.paddingBottom = 6;
            section.style.borderBottomWidth = 1;
            section.style.borderBottomColor = new Color(0.25f, 0.25f, 0.25f);

            var label = new Label(title);
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.marginBottom = 4;
            section.Add(label);
            return section;
        }

        private void Execute(string actionId)
        {
            if (_session == null)
            {
                return;
            }

            var descriptor = FindAction(actionId);
            if (descriptor.RequiresConfirmation
                && !EditorUtility.DisplayDialog(
                    "Confirm Workbench Action",
                    descriptor.Description,
                    "Run",
                    "Cancel"))
            {
                return;
            }

            if (!_session.TryExecuteAction(actionId, out var error))
            {
                EditorUtility.DisplayDialog("Workbench Action Failed", error, "OK");
            }

            Render();
        }

        private WorldGraphWorkbenchActionDescriptor FindAction(string actionId)
        {
            var snapshot = _session?.CurrentSnapshot;
            if (snapshot != null)
            {
                for (var i = 0; i < snapshot.Actions.Count; i++)
                {
                    if (snapshot.Actions[i].ActionId == actionId)
                    {
                        return snapshot.Actions[i];
                    }
                }
            }

            return new WorldGraphWorkbenchActionDescriptor(actionId, actionId, string.Empty, WorldGraphWorkbenchActionKind.ProjectCommand, WorldGraphWorkbenchActionRisk.None, string.Empty, false);
        }

        private static string Empty(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "<empty>" : value;
        }
    }
}
