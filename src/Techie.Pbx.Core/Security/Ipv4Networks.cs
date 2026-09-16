using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace Techie.Pbx.Core.Security
{
    /// <summary>
    /// A set of IPv4 networks in CIDR form and the one question asked of it: is this address in
    /// one of them? Used to decide which clients the local sign-in bypass trusts (D24), and it is
    /// the kind of check the Helper's firewall commands will need later, so it lives in Core.
    /// </summary>
    public class Ipv4Networks
    {
        public IReadOnlyList<IPNetwork> Networks { get; }

        /// <summary>
        /// Parses "10.8.20.0/24" style entries. A bare address means that address only, as if it
        /// had been written /32. Blank entries are skipped; anything else that will not parse is
        /// a <see cref="ValidationFailedException"/>, because a range nobody can read is a rule
        /// nobody can trust.
        /// </summary>
        public Ipv4Networks(IEnumerable<string>? cidrs)
        {
            var errors = new List<string>();
            var networks = new List<IPNetwork>();

            foreach (var entry in cidrs ?? Enumerable.Empty<string>())
            {
                var text = (entry ?? "").Trim();
                if (text.Length == 0)
                    continue;

                if (TryParse(text, out var network))
                    networks.Add(network);
                else
                    errors.Add($"'{text}' is not an IPv4 address or CIDR range.");
            }

            if (errors.Count > 0)
                throw new ValidationFailedException(errors);

            Networks = networks;
        }

        public bool Contains(IPAddress? address)
        {
            if (address == null)
                return false;

            // Kestrel reports an IPv4 client as ::ffff:10.8.20.5 when it listens dual stack.
            if (address.IsIPv4MappedToIPv6)
                address = address.MapToIPv4();

            if (address.AddressFamily != AddressFamily.InterNetwork)
                return false;

            foreach (var network in this.Networks)
            {
                if (network.Contains(address))
                    return true;
            }

            return false;
        }

        public override string ToString() => string.Join(", ", this.Networks);

        /// <summary>
        /// One entry. Host bits are allowed and ignored, so "10.8.20.5/24" means the /24 that
        /// address sits in rather than being rejected on a technicality.
        /// </summary>
        public static bool TryParse(string text, out IPNetwork network)
        {
            network = default;

            var parts = text.Split('/');
            if (parts.Length > 2)
                return false;

            if (!IPAddress.TryParse(parts[0], out var address) || address.AddressFamily != AddressFamily.InterNetwork)
                return false;

            var prefixLength = 32;
            if (parts.Length == 2 && (!int.TryParse(parts[1], out prefixLength) || prefixLength < 0 || prefixLength > 32))
                return false;

            network = new IPNetwork(Mask(address, prefixLength), prefixLength);
            return true;
        }

        private static IPAddress Mask(IPAddress address, int prefixLength)
        {
            var bytes = address.GetAddressBytes();
            var value = BinaryPrimitives.ReadUInt32BigEndian(bytes);
            var mask = prefixLength == 0 ? 0u : uint.MaxValue << (32 - prefixLength);

            BinaryPrimitives.WriteUInt32BigEndian(bytes, value & mask);
            return new IPAddress(bytes);
        }
    }
}
