using System.Collections.Generic;
using Maze.Core.Grid;

namespace Maze.Core.Validation
{
    /// <summary>ТЗ §92: Error = level is not ready; Warning = potential problem; Info = informational.</summary>
    public enum ValidationSeverity
    {
        Info = 0,
        Warning = 1,
        Error = 2,
    }

    public enum ValidationCategory
    {
        Structural = 0,
        Visual = 1,
        Gameplay = 2,
    }

    public sealed class ValidationIssue
    {
        public ValidationIssue(ValidationSeverity severity, ValidationCategory category, string code, string message,
            GridPosition? position = null, string entityId = null)
        {
            Severity = severity;
            Category = category;
            Code = code;
            Message = message;
            Position = position;
            EntityId = entityId;
        }

        public ValidationSeverity Severity { get; }
        public ValidationCategory Category { get; }

        /// <summary>Stable identifier from <see cref="ValidationCodes"/>; messages may change, codes do not.</summary>
        public string Code { get; }

        public string Message { get; }
        public GridPosition? Position { get; }
        public string EntityId { get; }

        public override string ToString() => $"[{Severity}] {Code}: {Message}";
    }

    public sealed class ValidationReport
    {
        /// <summary>Per-code limit so one broken setting does not produce thousands of identical issues.</summary>
        public const int MaxIssuesPerCode = 25;

        private readonly List<ValidationIssue> _issues = new List<ValidationIssue>();
        private readonly Dictionary<string, int> _countByCode = new Dictionary<string, int>();
        private readonly Dictionary<string, ValidationCategory> _categoryByCode = new Dictionary<string, ValidationCategory>();

        public IReadOnlyList<ValidationIssue> Issues => _issues;
        public int ErrorCount { get; private set; }
        public int WarningCount { get; private set; }

        /// <summary>Level can be considered ready only without errors.</summary>
        public bool IsValid => ErrorCount == 0;

        public bool Has(string code) => _countByCode.ContainsKey(code);

        /// <summary>Total occurrences of a code, including ones beyond <see cref="MaxIssuesPerCode"/>.</summary>
        public int Count(string code) => _countByCode.TryGetValue(code, out var count) ? count : 0;

        internal void Add(ValidationSeverity severity, ValidationCategory category, string code, string message,
            GridPosition? position = null, string entityId = null)
        {
            _countByCode.TryGetValue(code, out var count);
            _countByCode[code] = count + 1;
            _categoryByCode[code] = category;

            if (severity == ValidationSeverity.Error) ErrorCount++;
            else if (severity == ValidationSeverity.Warning) WarningCount++;

            if (count < MaxIssuesPerCode)
                _issues.Add(new ValidationIssue(severity, category, code, message, position, entityId));
        }

        internal void AddTruncationNotes()
        {
            foreach (var pair in _countByCode)
                if (pair.Value > MaxIssuesPerCode)
                    _issues.Add(new ValidationIssue(ValidationSeverity.Info, _categoryByCode[pair.Key], pair.Key,
                        $"{pair.Value - MaxIssuesPerCode} more '{pair.Key}' issues not listed."));
        }
    }
}
