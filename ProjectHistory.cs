using System;
using System.Collections.Generic;

namespace GalgameUiTranslator
{
    public sealed class ProjectHistory
    {
        private readonly List<string> _snapshots = new List<string>();
        private readonly int _maximumSnapshots;
        private int _index = -1;

        public ProjectHistory(int maximumSnapshots = 30)
        {
            _maximumSnapshots = Math.Max(2, maximumSnapshots);
        }

        public bool CanUndo => _index > 0;
        public bool CanRedo => _index >= 0 && _index < _snapshots.Count - 1;

        public void Reset(TranslationProject project)
        {
            _snapshots.Clear();
            _index = -1;
            Capture(project);
        }

        public bool Capture(TranslationProject project)
        {
            if (project == null) return false;
            var updatedAt = project.UpdatedAt;
            string snapshot;
            try
            {
                project.UpdatedAt = DateTime.MinValue;
                snapshot = ProjectService.SerializeProject(project);
            }
            finally
            {
                project.UpdatedAt = updatedAt;
            }
            if (_index >= 0 && string.Equals(_snapshots[_index], snapshot, StringComparison.Ordinal))
                return false;

            if (_index < _snapshots.Count - 1)
                _snapshots.RemoveRange(_index + 1, _snapshots.Count - _index - 1);

            _snapshots.Add(snapshot);
            _index = _snapshots.Count - 1;
            if (_snapshots.Count > _maximumSnapshots)
            {
                _snapshots.RemoveAt(0);
                _index--;
            }
            return true;
        }

        public TranslationProject Undo()
        {
            if (!CanUndo) return null;
            _index--;
            return ProjectService.DeserializeProject(_snapshots[_index]);
        }

        public TranslationProject Redo()
        {
            if (!CanRedo) return null;
            _index++;
            return ProjectService.DeserializeProject(_snapshots[_index]);
        }
    }
}
