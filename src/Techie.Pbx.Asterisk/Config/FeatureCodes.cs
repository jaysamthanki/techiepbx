using System.Globalization;

namespace Techie.Pbx.Asterisk.Config
{
    /// <summary>
    /// Every code a user can dial on this system, as the cheat sheet prints them (D120).
    ///
    /// The single rule this class follows: a code appears here only when the generated config
    /// really carries it, and it is spelled from the same constant the renderer writes. *2 and #
    /// come from <see cref="FeaturesConfRenderer"/>, *43 and *97 from
    /// <see cref="ExtensionsConfRenderer"/>, the park code and the slot numbers from
    /// <see cref="ParkingSettings"/>. Nothing is typed twice, so a sheet on a wall cannot promise
    /// a code that was switched off six months ago.
    ///
    /// Deliberately not validated, unlike every renderer: nothing here is written to a conf file,
    /// and a settings row that is somehow out of range must not be able to take a read-only page
    /// down. <see cref="AsteriskSettings.Parking"/> has already clamped what it could not read.
    /// </summary>
    public static class FeatureCodes
    {
        /// <summary>Codes that only do anything while you are already talking to somebody.</summary>
        public const string DuringACall = "During a call";

        /// <summary>Codes you dial the way you would dial an extension.</summary>
        public const string FromYourPhone = "From your phone";

        /// <param name="parking">
        /// Call parking as the generated config has it. Switched off means no park code and no
        /// retrieval slots on the sheet at all, because neither exists in the dialplan (D119).
        /// </param>
        /// <param name="voicemailInUse">
        /// Whether any enabled extension has a mailbox, which is the same test
        /// <see cref="ExtensionsConfRenderer"/> makes before it writes *97. Nobody having a
        /// mailbox means the code would only ever say "no such mailbox", so it is not generated
        /// and not printed.
        /// </param>
        public static List<FeatureCode> All(ParkingSettings parking, bool voicemailInUse)
        {
            var codes = new List<FeatureCode>
            {
                new()
                {
                    Code = FeaturesConfRenderer.AttendedTransferCode,
                    Description = "Press the code, dial the other extension and speak to them first. " +
                        "Hang up to hand the call over.",
                    Group = DuringACall,
                    Name = "Transfer, announced",
                },
                new()
                {
                    // Not in features.conf: it is Asterisk's default for blindxfer, turned on by
                    // the t/T in every generated Dial (D119). Real, and easy to press by accident,
                    // which is its own reason for printing it.
                    Code = FeaturesConfRenderer.BlindTransferCode,
                    Description = "Press #, dial the other extension, and the call goes straight there " +
                        "without you speaking to them first.",
                    Group = DuringACall,
                    Name = "Transfer, straight through",
                },
            };

            if (parking.Enabled)
            {
                codes.Add(new FeatureCode
                {
                    Code = parking.DtmfCode,
                    Description = "The system reads a slot number back to you. Anyone can pick the call " +
                        "up by dialling that number. If nobody does within " +
                        $"{Seconds(parking.TimeoutSeconds)}, it rings your phone again.",
                    Group = DuringACall,
                    Name = "Park the call",
                });

                codes.Add(new FeatureCode
                {
                    Code = SlotRange(parking),
                    Description = "Dial the slot number you were told. It is a single digit, so press the " +
                        "dial or # key straight after it — otherwise the phone waits a moment before sending it.",
                    Group = FromYourPhone,
                    Name = "Pick up a parked call",
                });
            }

            if (voicemailInUse)
            {
                codes.Add(new FeatureCode
                {
                    Code = ExtensionsConfRenderer.VoicemailMainNumber,
                    Description = "Listen to the messages left on your own extension. It asks for your PIN.",
                    Group = FromYourPhone,
                    Name = "Your voicemail",
                });
            }

            codes.Add(new FeatureCode
            {
                Code = ExtensionsConfRenderer.EchoTestNumber,
                Description = "Hear your own voice played straight back, to check the phone and the line. " +
                    "Hang up to end it.",
                Group = FromYourPhone,
                Name = "Echo test",
            });

            return codes;
        }

        /// <summary>A whole number of seconds, written the way a sentence wants it.</summary>
        private static string Seconds(int seconds) =>
            $"{seconds.ToString(CultureInfo.InvariantCulture)} seconds";

        /// <summary>
        /// The slot numbers as one entry rather than nine: they are consecutive single digits by
        /// construction (<see cref="ParkingSettings.SlotNumbers"/>), and nine rows saying the same
        /// thing is nine rows a one-page sheet has not got.
        /// </summary>
        private static string SlotRange(ParkingSettings parking) =>
            parking.Slots <= 1
                ? "1"
                : $"1-{parking.Slots.ToString(CultureInfo.InvariantCulture)}";
    }
}
