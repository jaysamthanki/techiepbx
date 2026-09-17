namespace Techie.Pbx.Core.Models
{
    /// <summary>
    /// The two kinds of rule a time condition is built from. A weekly rule says when the site is
    /// open; a holiday rule says when it is not, whatever the weekly rules say (D63).
    ///
    /// Stored as the number, not the name, which is the one place this differs from
    /// <see cref="DestinationType"/> — the values are written here explicitly so that reordering
    /// this list cannot change what a stored row means.
    /// </summary>
    public enum TimeConditionRuleKind
    {
        /// <summary>Days of the week and a time range, e.g. "09:00-17:00, Monday to Friday".</summary>
        Weekly = 0,

        /// <summary>One date, e.g. "25 December". It comes round every year (D64).</summary>
        Holiday = 1,
    }
}
