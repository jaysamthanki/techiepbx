using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Core.Migration
{
    /// <summary>One phone the import will create, and its keys: the line first, then the lamps.</summary>
    public class PlannedPhone
    {
        public List<PhoneButton> Buttons { get; set; } = new();
        public Phone Phone { get; set; } = new();
    }
}
